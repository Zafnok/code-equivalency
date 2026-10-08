using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// An API-equivalence adapter item with an integer range, and an entry with the runtime that added its modern member
/// (ADR 0020, clarified for ticket P2-142). The test compilation references .NET 10, where <c>TimeSpan.FromHours(2)</c>
/// already binds the integer overload, so the legacy members are stubs that take a <c>double</c>, and the catalogue's own
/// adapters are applied to them.
/// </summary>
public sealed class IntegerRangeLoweringTests
{
    /// <summary>A stand-in for the <c>TimeSpan</c> factories as .NET 8 and .NET Framework have them: one <c>double</c> overload each.</summary>
    private const string Spans = """
        struct T
        {
            public static T FromDays(double value) => default;
            public static T FromHours(double value) => default;
            public static T FromMinutes(double value) => default;
            public static T FromSeconds(double value) => default;
            public static T FromMilliseconds(double value) => default;
            public static T FromObject(object value) => default;
        }

        """;

    private static readonly Uri Page = new("https://learn.microsoft.com/x");

    /// <summary>The catalogue's five <c>TimeSpan</c> entries, each with its legacy member the stand-in's.</summary>
    private static ImmutableArray<ApiEquivalence> Factories =>
    [
        .. ApiEquivalenceTable.Load().Entries
            .Where(static e => e.Id.StartsWith("bcl.timespan-from-", StringComparison.Ordinal))
            .Select(static e => e with { Legacy = e.Legacy.Replace("System.TimeSpan::", "T::", StringComparison.Ordinal) }),
    ];

    private static (IrProcedure Body, ImmutableArray<string> Applied) Lower(string parameters, string call, ImmutableArray<ApiEquivalence> entries, SideRuntime? runtime = null)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Spans + $"class C {{ const int Twelve = 12; static T M({parameters}) => {call}; }}");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        (IrProcedure procedure, ImmutableArray<string> applied) = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], entries, runtime ?? Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        return (procedure, applied);
    }

    private static IrInstruction Definition(IrProcedure body, IrVar value) =>
        body.Blocks.SelectMany(static b => b.Instructions).Single(i => i switch
        {
            IrConst constant => constant.Target == value,
            IrUnary unary => unary.Target == value,
            _ => false,
        });

    /// <summary>Criterion 3: a call of each of the five pairs with a constant in range is a call to the integer overload.</summary>
    [Theory]
    [InlineData("T.FromDays(7)", "System.TimeSpan::FromDays(int)", 32, 7, "bcl.timespan-from-days-integer")]
    [InlineData("T.FromHours(2)", "System.TimeSpan::FromHours(int)", 32, 2, "bcl.timespan-from-hours-integer")]
    [InlineData("T.FromHours(Twelve)", "System.TimeSpan::FromHours(int)", 32, 12, "bcl.timespan-from-hours-integer")]
    [InlineData("T.FromHours(Twelve * 2)", "System.TimeSpan::FromHours(int)", 32, 24, "bcl.timespan-from-hours-integer")]
    [InlineData("T.FromHours(-256204778)", "System.TimeSpan::FromHours(int)", 32, -256204778, "bcl.timespan-from-hours-integer")]
    [InlineData("T.FromHours(256204778)", "System.TimeSpan::FromHours(int)", 32, 256204778, "bcl.timespan-from-hours-integer")]
    [InlineData("T.FromDays(-10675199)", "System.TimeSpan::FromDays(int)", 32, -10675199, "bcl.timespan-from-days-integer")]
    [InlineData("T.FromDays(10675199)", "System.TimeSpan::FromDays(int)", 32, 10675199, "bcl.timespan-from-days-integer")]
    [InlineData("T.FromMinutes(30)", "System.TimeSpan::FromMinutes(long)", 64, 30, "bcl.timespan-from-minutes-integer")]
    [InlineData("T.FromSeconds(10)", "System.TimeSpan::FromSeconds(long)", 64, 10, "bcl.timespan-from-seconds-integer")]
    [InlineData("T.FromSeconds(10L)", "System.TimeSpan::FromSeconds(long)", 64, 10, "bcl.timespan-from-seconds-integer")]
    [InlineData("T.FromSeconds(-2147483648)", "System.TimeSpan::FromSeconds(long)", 64, -2147483648, "bcl.timespan-from-seconds-integer")]
    [InlineData("T.FromSeconds(2147483647)", "System.TimeSpan::FromSeconds(long)", 64, 2147483647, "bcl.timespan-from-seconds-integer")]
    public void AConstantInRange_IsACallToTheIntegerOverload(string call, string modern, int bits, long value, string id)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(string.Empty, call, Factories);

        IrCall lowered = Assert.Single(Calls(body));
        Assert.Equal(modern, lowered.Callee.Value);
        IrVar argument = Assert.Single(lowered.Args);
        Assert.Equal(new IrBitVec(bits), argument.Type);
        Assert.Equal([id], applied);
        Assert.Equal(Bits(bits, value), Constant(body, argument));
    }

    /// <summary>The constant's value at the adapted width: the constant itself, or the constant a sign or zero extension widens.</summary>
    private static IrValue Constant(IrProcedure body, IrVar value) => Definition(body, value) switch
    {
        IrConst constant => constant.Value,
        IrUnary { Op: IrUnaryOp.SExt } unary => Bits(((IrBitVec)value.Type).Width, ((IrBitVecValue)((IrConst)Definition(body, unary.A)).Value).TwosComplement),
        var other => throw new InvalidOperationException(other.ToString()),
    };

    /// <summary>
    /// On a pair whose modern side is .NET 9, <c>FromMilliseconds(long, long)</c> is called with its second argument's
    /// default, 0 microseconds; .NET 10 has <c>FromMilliseconds(long)</c>, whose entry comes first.
    /// </summary>
    [Fact]
    public void MillisecondsOfAConstantOnDotNet9_PassTheDefaultMicroseconds()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(string.Empty, "T.FromMilliseconds(250)", Factories, Runtimes.Between("net8.0", "net9.0"));

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.TimeSpan::FromMilliseconds(long,long)", call.Callee.Value);
        Assert.Equal([new IrBitVec(64), new IrBitVec(64)], call.Args.Select(static a => a.Type));
        Assert.Equal(Bits(64, 250), Constant(body, call.Args[0]));
        Assert.Equal(Bits(64, 0), Constant(body, call.Args[1]));
        Assert.Equal(["bcl.timespan-from-milliseconds-integer-and-microseconds"], applied);
    }

    [Fact]
    public void MillisecondsOfAConstantOnDotNet10_AreACallToTheOverloadItAdded()
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(string.Empty, "T.FromMilliseconds(-1)", Factories);

        IrCall call = Assert.Single(Calls(body));
        Assert.Equal("System.TimeSpan::FromMilliseconds(long)", call.Callee.Value);
        Assert.Equal(Bits(64, -1), Constant(body, Assert.Single(call.Args)));
        Assert.Equal(["bcl.timespan-from-milliseconds-integer"], applied);
    }

    /// <summary>
    /// Criterion 4: a constant outside the range, where the two overloads throw different exception types, a constant
    /// whose type does not convert implicitly to the integer overload's parameter, and a floating-point argument stay calls
    /// to the legacy member.
    /// </summary>
    [Theory]
    [InlineData("T.FromHours(256204779)", "T::FromHours(double)")]
    [InlineData("T.FromHours(-256204779)", "T::FromHours(double)")]
    [InlineData("T.FromDays(10675200)", "T::FromDays(double)")]
    [InlineData("T.FromDays(-10675200)", "T::FromDays(double)")]
    [InlineData("T.FromSeconds(2147483648)", "T::FromSeconds(double)")]
    [InlineData("T.FromSeconds(-2147483649)", "T::FromSeconds(double)")]
    [InlineData("T.FromHours(2L)", "T::FromHours(double)")]
    [InlineData("T.FromHours(2u)", "T::FromHours(double)")]
    [InlineData("T.FromSeconds(2ul)", "T::FromSeconds(double)")]
    [InlineData("T.FromSeconds('a')", "T::FromSeconds(double)")]
    [InlineData("T.FromHours(2.0)", "T::FromHours(double)")]
    [InlineData("T.FromHours(2f)", "T::FromHours(double)")]
    public void AConstantOutOfRangeOrOfAnotherType_IsNotRewritten(string call, string legacy)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(string.Empty, call, Factories);

        Assert.Equal(legacy, Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    /// <summary>
    /// Criterion 4: an argument that is not constant is some value of its type, and an <c>int</c> of hours or days can be out
    /// of range, as can a <c>long</c> or a <c>uint</c> of seconds, so those calls stay calls to the legacy member.
    /// </summary>
    [Theory]
    [InlineData("int n", "T.FromHours(n)", "T::FromHours(double)")]
    [InlineData("int n", "T.FromDays(n)", "T::FromDays(double)")]
    [InlineData("long n", "T.FromSeconds(n)", "T::FromSeconds(double)")]
    [InlineData("uint n", "T.FromSeconds(n)", "T::FromSeconds(double)")]
    [InlineData("ulong n", "T.FromSeconds(n)", "T::FromSeconds(double)")]
    [InlineData("long n", "T.FromMilliseconds(n)", "T::FromMilliseconds(double)")]
    [InlineData("double n", "T.FromMinutes(n)", "T::FromMinutes(double)")]
    public void AnArgumentThatMayBeOutOfRange_IsNotRewritten(string parameter, string call, string legacy)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(parameter, call, Factories);

        Assert.Equal(legacy, Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    /// <summary>
    /// An argument that is not constant, of a type all of whose values are in range, is rewritten, and widened as the
    /// implicit conversion the modern side writes is: by its own type's signedness.
    /// </summary>
    [Theory]
    [InlineData("int n", "T.FromSeconds(n)", "System.TimeSpan::FromSeconds(long)", 64, IrUnaryOp.SExt)]
    [InlineData("int n", "T.FromMinutes(n)", "System.TimeSpan::FromMinutes(long)", 64, IrUnaryOp.SExt)]
    [InlineData("int n", "T.FromMilliseconds(n)", "System.TimeSpan::FromMilliseconds(long)", 64, IrUnaryOp.SExt)]
    [InlineData("short n", "T.FromHours(n)", "System.TimeSpan::FromHours(int)", 32, IrUnaryOp.SExt)]
    [InlineData("sbyte n", "T.FromDays(n)", "System.TimeSpan::FromDays(int)", 32, IrUnaryOp.SExt)]
    [InlineData("ushort n", "T.FromHours(n)", "System.TimeSpan::FromHours(int)", 32, IrUnaryOp.ZExt)]
    [InlineData("byte n", "T.FromSeconds(n)", "System.TimeSpan::FromSeconds(long)", 64, IrUnaryOp.ZExt)]
    public void AnArgumentWhoseTypeIsInRange_IsWidenedToTheIntegerOverload(string parameter, string call, string modern, int bits, IrUnaryOp widening)
    {
        (IrProcedure body, ImmutableArray<string> applied) = Lower(parameter, call, Factories);

        IrCall lowered = Assert.Single(Calls(body));
        Assert.Equal(modern, lowered.Callee.Value);
        Assert.Equal(new IrBitVec(bits), lowered.Args[0].Type);
        IrUnary widened = Assert.IsType<IrUnary>(Definition(body, lowered.Args[0]));
        Assert.Equal(widening, widened.Op);
        Assert.Equal("n", widened.A.SourceName);
        Assert.Single(applied);
    }

    /// <summary>A range as wide as the argument's type and width takes every value of it, with no widening.</summary>
    [Theory]
    [InlineData("int n", 32, int.MinValue, int.MaxValue, true)]
    [InlineData("int n", 32, int.MinValue + 1L, int.MaxValue, false)]
    [InlineData("int n", 32, int.MinValue, int.MaxValue - 1L, false)]
    [InlineData("long n", 64, long.MinValue, long.MaxValue, true)]
    [InlineData("uint n", 32, long.MinValue, long.MaxValue, false)]
    [InlineData("uint n", 64, 0, uint.MaxValue, true)]
    [InlineData("long n", 32, long.MinValue, long.MaxValue, false)]
    [InlineData("short n", 8, long.MinValue, long.MaxValue, false)]
    [InlineData("sbyte n", 8, sbyte.MinValue, sbyte.MaxValue, true)]
    public void ARangeIsComparedWithEveryValueOfTheArgumentsType(string parameter, int bits, long min, long max, bool rewritten)
    {
        ApiEquivalence entry = new("t", IsType: false, "T::FromHours(double)", "T::G()", [new ApiArgument(0, Unwrap: true) { Range = new ApiIntegerRange(bits, min, max) }], "r", Page);

        (IrProcedure body, ImmutableArray<string> applied) = Lower(parameter, "T.FromHours(n)", [entry]);

        Assert.Equal(rewritten ? "T::G()" : "T::FromHours(double)", Assert.Single(Calls(body)).Callee.Value);
        Assert.Equal(rewritten, applied.Contains("t", StringComparer.Ordinal));
    }

    /// <summary>An argument as wide as the range's width is passed as it is.</summary>
    [Fact]
    public void AnArgumentOfTheRangesWidth_IsNotWidened()
    {
        ApiEquivalence entry = new("t", IsType: false, "T::FromHours(double)", "T::G(int)", [new ApiArgument(0, Unwrap: true) { Range = new ApiIntegerRange(32, int.MinValue, int.MaxValue) }], "r", Page);

        IrProcedure body = Lower("int n", "T.FromHours(n)", [entry]).Body;

        Assert.Equal(["n"], Assert.Single(Calls(body)).Args.Select(static a => a.SourceName), StringComparer.Ordinal);
    }

    /// <summary>An item with a range on an argument that has no type, or that is taken with its conversion, addresses nothing.</summary>
    [Theory]
    [InlineData("T.FromObject(null)", "T::FromObject(object)", true)]
    [InlineData("T.FromHours(2)", "T::FromHours(double)", false)]
    public void ARangeOnAnArgumentThatIsNoInteger_LeavesTheCallAsItIs(string call, string legacy, bool unwrap)
    {
        ApiEquivalence entry = new("t", IsType: false, legacy, "T::G()", [new ApiArgument(0, unwrap) { Range = new ApiIntegerRange(64, long.MinValue, long.MaxValue) }], "r", Page);

        (IrProcedure body, ImmutableArray<string> applied) = Lower(string.Empty, call, [entry]);

        Assert.Equal(legacy, Assert.Single(Calls(body)).Callee.Value);
        Assert.Empty(applied);
    }

    /// <summary>
    /// An entry whose modern member a runtime added applies only to a pair that crosses that runtime: on .NET Framework 4.8
    /// to .NET 8 both sides bind the <c>double</c> overload, and a rewrite would make the call a rebound one.
    /// </summary>
    [Theory]
    [InlineData("net48", "net8.0", "T::FromHours(double)")]
    [InlineData("net9.0", "net10.0", "T::FromHours(double)")]
    [InlineData("net8.0", "net9.0", "System.TimeSpan::FromHours(int)")]
    [InlineData("net48", "net10.0", "System.TimeSpan::FromHours(int)")]
    public void AnEntryWithARuntimeThatAddedItsModernMember_AppliesOnlyToAPairThatCrossesIt(string legacy, string modern, string callee)
    {
        (IrProcedure body, _) = Lower(string.Empty, "T.FromHours(2)", Factories, Runtimes.Between(legacy, modern));

        Assert.Equal(callee, Assert.Single(Calls(body)).Callee.Value);
    }
}
