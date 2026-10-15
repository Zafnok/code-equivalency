using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0041 (ticket P2-060): a call whose callee's containing type, parameter types and type arguments are all inert is
/// closed and gets no heap pairs, in both lowerings; every other call pairs each heap map its body touches.
/// </summary>
public sealed class ClosedCallsTests
{
    public static TheoryData<string, bool> Calls => new()
    {
        { "static string M(string s) { f++; return string.Concat(s, \"t\"); }", true },
        { "static string M(string s) { f++; return s + \"t\"; }", true },
        { "static string M(string s) { f++; return s.Trim(); }", true },
        { "static int M(int? x) { f++; return x.GetValueOrDefault(); }", true },
        { "static bool M(DayOfWeek? d) { f++; return d.HasValue; }", true },
        { "static bool M(string s) { f++; return int.TryParse(s, out _); }", true },
        { "static long M(long x) { f++; return int.CreateChecked(x); }", true },
        { "static bool M(string s) { f++; return bool.Parse(s); }", true },
        { "static bool M(char c) { f++; return char.IsDigit(c); }", true },
        { "static sbyte M(string s) { f++; return sbyte.Parse(s); }", true },
        { "static byte M(string s) { f++; return byte.Parse(s); }", true },
        { "static short M(string s) { f++; return short.Parse(s); }", true },
        { "static ushort M(string s) { f++; return ushort.Parse(s); }", true },
        { "static uint M(string s) { f++; return uint.Parse(s); }", true },
        { "static ulong M(string s) { f++; return ulong.Parse(s); }", true },
        { "static float M(string s) { f++; return float.Parse(s); }", true },
        { "static double M(string s) { f++; return double.Parse(s); }", true },
        { "static decimal M(string s) { f++; return decimal.Parse(s); }", true },
        { "static string M(object o) { f++; return string.Concat(o, o); }", false },
        { "static string M(string[] a) { f++; return string.Concat(a); }", false },
        { "static bool M(DateTime? t) { f++; return t.HasValue; }", false },
        { "static void M(string s) { f++; Console.WriteLine(s); }", false },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void ACallIsClosedWhenEverythingItIsHandedIsInert(string method, bool closed)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\nstatic int f;\n{method}\n}}\n");
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        foreach (IrProcedure procedure in new[] { IrLowerer.Lower(symbol, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(symbol, compilation, Runtimes.Migration) })
        {
            Assert.Empty(IrValidator.Validate(procedure));
            IrCall call = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());
            Assert.Equal(closed, call.Closed);
            Assert.Equal(closed, call.Heap.IsEmpty);
        }
    }

    /// <summary>
    /// Ticket P2-184: the rule read from a member's identity, as a call an API-equivalence entry rewrites to it names it.
    /// A type that does not resolve, a generic type and a generic containing type are not inert.
    /// </summary>
    [Theory]
    [InlineData("System.String::Split(char,System.StringSplitOptions)", true)]
    [InlineData("System.String::TrimEnd()", true)]
    [InlineData("System.String::Contains(char)", true)]
    [InlineData("System.Int32::TryParse(string,out int)", true)]
    [InlineData("System.Int32::M(ref System.DayOfWeek?)", true)]
    [InlineData("System.String::M`1(int?)<bool>", true)]
    [InlineData("System.String::M`1(int)<object>", false)]
    [InlineData("System.String::M(System.DateTime?)", false)]
    [InlineData("System.String::Join(char,System.ReadOnlySpan<string>)", false)]
    [InlineData("System.String::Format(System.IFormatProvider,string,System.ReadOnlySpan<object>)", false)]
    [InlineData("System.String::M((int, int))", false)]
    [InlineData("System.IO.FileSystemInfo::get_FullName()", false)]
    [InlineData("System.Enum::IsDefined`1(System.DayOfWeek)<System.DayOfWeek>", false)]
    [InlineData("System.Nullable`1::GetValueOrDefault()<int>", false)]
    [InlineData("No.Such.Type::M()", false)]
    public void AnIdentityNamesAClosedMemberWhenEveryTypeItNamesIsInert(string identity, bool closed) =>
        Assert.Equal(closed, ClosedCalls.IsClosed(new CallIdentity(identity), RoslynTestCompilations.Compile("class C { }")));

    public static TheoryData<string, bool> Throwing => new()
    {
        { "static string M(string s) => string.Concat(s, \"t\");", false },
        { "static string M(string s, string t) => string.Concat(s, t, s);", false },
        { "static string M(string s, string t) => string.Concat(s, t, s, t);", false },
        { "static string M(string s) => s + \"t\";", false },
        { "static bool M(string s) => string.IsNullOrEmpty(s);", false },
        { "static bool M(string s) => string.IsNullOrWhiteSpace(s);", false },
        { "static string M(string s) => s.ToUpper();", false },
        { "static string M(string s) => s.ToLower();", false },
        { "static string M(string s) => s.ToUpperInvariant();", false },
        { "static string M(string s) => s.ToLowerInvariant();", false },
        { "static string M(string s) => s.Trim();", false },
        { "static string M(string s) => s.TrimStart();", false },
        { "static string M(string s) => s.TrimEnd();", false },
        { "static string M(string s) => s.Trim('.');", false },
        { "static string M(string s) => s.TrimStart('.');", false },
        { "static string M(string s) => s.TrimEnd('.');", false },
        { "static string M(int i) => i.ToString();", false },
        { "static string M(ulong i) => i.ToString();", false },
        { "static string M(bool b) => b.ToString();", false },
        { "static int M(int? x) => x.GetValueOrDefault();", false },
        { "static int M(int? x) => x.GetValueOrDefault(3);", false },
        { "static bool M(DateTime? t) => t.HasValue;", false },
        { "static int M(int? x) => x.Value;", true },
        { "static int M(string s) => int.Parse(s);", true },
        { "static string M(string s) => s.Substring(1);", true },
        { "static string M(string s, System.Globalization.CultureInfo c) => s.ToUpper(c);", true },
        { "static string M(int i) => i.ToString(\"x\");", true },
        { "static string M(double d) => d.ToString();", true },
        { "static void M(string s) => Console.WriteLine(s);", true },
    };

    /// <summary>
    /// ADR 0056 (ticket P2-081): a call to a listed member that throws for no argument has no <c>threw</c> flag, in both
    /// lowerings, so a side does not branch on a flag the replay taints; every other call keeps its flag.
    /// </summary>
    [Theory]
    [MemberData(nameof(Throwing))]
    public void ACallHasAThrewFlagUnlessItsMemberNeverThrows(string method, bool flag)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{method}\n}}\n");
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        foreach (IrProcedure procedure in new[] { IrLowerer.Lower(symbol, compilation, RenameMap.Empty, [], Runtimes.Migration), IlLowerer.Lower(symbol, compilation, Runtimes.Migration) })
        {
            Assert.Empty(IrValidator.Validate(procedure));
            IrCall call = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());
            Assert.Equal(flag, call.Threw is not null);
        }
    }

    /// <summary>From IL a conversion to <c>int?</c> is a call to <c>Nullable&lt;int&gt;</c>'s constructor, which never throws.</summary>
    [Fact]
    public void ANullableConstructorFromIlHasNoThrewFlag()
    {
        Compilation compilation = RoslynTestCompilations.Compile("class C { static int? M(int x) => x; }");
        IMethodSymbol symbol = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        IrCall call = Assert.Single(IlLowerer.Lower(symbol, compilation, Runtimes.Migration).Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());

        Assert.StartsWith("System.Nullable`1::.ctor(", call.Callee.Value, StringComparison.Ordinal);
        Assert.Null(call.Threw);
    }
}
