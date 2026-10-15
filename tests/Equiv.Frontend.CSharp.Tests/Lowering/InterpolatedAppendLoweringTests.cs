using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-164: an interpolated string passed to <c>StringBuilder.Append</c> or <c>AppendLine</c> binds <c>Append(string)</c>
/// before .NET 6 (C# 9 here) and <c>Append(ref StringBuilder.AppendInterpolatedStringHandler)</c> from .NET 6 (C# 10). Where
/// no hole can tell the handler's piece-by-piece order from the joined string's, the handler binding lowers to the call of
/// the <c>string</c> member with the joined parts, so the two bindings make the same calls with the same arguments in the
/// same order. Every other shape keeps the handler's calls.
/// </summary>
public sealed class InterpolatedAppendLoweringTests
{
    private const string Handler = "System.Text.StringBuilder.AppendInterpolatedStringHandler::";

    private const string Helpers = "static string F() => \"f\";\nstatic void G(StringBuilder b) { }\nstatic StringBuilder Make() => new();\nStringBuilder Get() => new();\nsealed class K { }\n";

    /// <summary>
    /// Criterion 1's shapes and the others the rule takes: a builder of any kind with holes that are <c>string</c> or
    /// integer reads, and a builder no code outside the body reaches, whose holes may run code. Run on the same inputs,
    /// once with a builder that is not null and once with a null one, both bindings make the same calls with the same
    /// arguments and end the same way.
    /// </summary>
    [Theory]
    [InlineData("static void M(StringBuilder sb, string id) { sb.Append($\"index {id};\"); }")]
    [InlineData("static void M(StringBuilder sb, int n) { sb.Append($\"n={n};\"); }")]
    [InlineData("static void M(StringBuilder sb, string id) { sb.AppendLine($\"id {id}\"); }")]
    [InlineData("static StringBuilder M(StringBuilder sb, string s, long n) => sb.Append($\"{s}\").Append($\"{n}\");")]
    [InlineData("string f; void M(StringBuilder sb, int n) { F(); sb.Append(\"a\"); sb.Append($\"{n}{f}{7}\"); }")]
    [InlineData("static string M(string s) { int k = 1; StringBuilder sb = new(); sb.Append(\"a\").Append($\"x{F()}y{s}\"); sb.AppendLine($\"{F()}{k}\"); return sb.Length > k ? sb.ToString() : null; }")]
    [InlineData("static string M(string s, string t) { var sb = new StringBuilder(); sb.Append($\"a{s ?? t}b\"); return sb.ToString(); }")]
    [InlineData("static string M(K k, string s) { var sb = new StringBuilder(); sb.Append($\"a{k}b{s}\"); return sb.ToString(); }")]
    public void BothBindingsMakeTheSameCalls(string members)
    {
        IrProcedure legacy = Bound(members, LanguageVersion.CSharp9);
        IrProcedure modern = Bound(members, LanguageVersion.CSharp10);

        Assert.Empty(Opaques(legacy));
        Assert.Empty(Opaques(modern));
        Assert.DoesNotContain(Calls(modern), static c => c.Callee.Value.StartsWith(Handler, StringComparison.Ordinal));
        foreach (bool isNull in (bool[])[false, true])
        {
            Assert.Equal(Trace(legacy, isNull), Trace(modern, isNull));
        }
    }

    /// <summary>
    /// Criterion 2: a hole that reads the builder keeps the handler's calls, whether the builder is a parameter, whose
    /// hole then runs code, or a local, which the hole names.
    /// </summary>
    [Theory]
    [InlineData("static void M(StringBuilder sb) { sb.Append($\"{sb.Length}\"); }")]
    [InlineData("static string M() { var sb = new StringBuilder(); sb.Append($\"x{sb.Length}\"); return sb.ToString(); }")]
    [InlineData("static string M() { var sb = new StringBuilder(); sb.Append($\"x{F()}{sb}\"); return sb.ToString(); }")]
    public void AHoleThatReadsTheBuilderKeepsTheHandler(string members) => AssertKept(members);

    /// <summary>Criterion 3: the overloads that take an <c>IFormatProvider</c> keep the handler's calls.</summary>
    [Theory]
    [InlineData("static void M(StringBuilder sb, string s) { sb.Append(System.Globalization.CultureInfo.InvariantCulture, $\"x{s}\"); }")]
    [InlineData("static void M(StringBuilder sb, string s) { sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $\"x{s}\"); }")]
    public void AFormatProviderKeepsTheHandler(string members) => AssertKept(members);

    /// <summary>
    /// A clause, a hole P2-086 and P2-139 do not join, and, on a builder code outside the body may reach, a hole that runs
    /// code or is a class whose <c>ToString()</c> does, keep the handler's calls.
    /// </summary>
    [Theory]
    [InlineData("static void M(StringBuilder sb, int n) { sb.Append($\"x{n:D}\"); }")]
    [InlineData("static void M(StringBuilder sb, int n) { sb.Append($\"x{n,5}\"); }")]
    [InlineData("static void M(StringBuilder sb, double d) { sb.Append($\"x{d}\"); }")]
    [InlineData("static void M(StringBuilder sb) { sb.Append($\"x{F()}\"); }")]
    [InlineData("static void M(StringBuilder sb, K k) { sb.Append($\"x{k}\"); }")]
    [InlineData("static void M(StringBuilder sb, string s) { sb.Append($\"x{s}\" + $\"y{s}\"); }")]
    public void AHoleThatCanTellTheOrdersApartKeepsTheHandler(string members) => AssertKept(members);

    /// <summary>
    /// A builder that code outside the body may reach, or that a <c>catch</c> or <c>finally</c> of the body may read after a
    /// hole throws, keeps the handler's calls when a hole runs code: it is handed to a call, returned, kept as the result of
    /// one of its calls, named in a lambda or a local function, appended to inside a <c>try</c>, not made by a <c>new</c>
    /// where it is declared, or not a local at all.
    /// </summary>
    [Theory]
    [InlineData("static void M() { var sb = new StringBuilder(); G(sb); sb.Append($\"x{F()}\"); }")]
    [InlineData("static StringBuilder M() { var sb = new StringBuilder(); sb.Append($\"x{F()}\"); return sb; }")]
    [InlineData("static void M() { var sb = new StringBuilder(); var t = sb.Append(\"a\"); sb.Append($\"x{F()}\"); }")]
    [InlineData("static void M() { var sb = new StringBuilder(); Action a = () => sb.Append(\"a\"); sb.Append($\"x{F()}\"); }")]
    [InlineData("static void M() { var sb = new StringBuilder(); void L() => sb.Append(\"a\"); sb.Append($\"x{F()}\"); }")]
    [InlineData("static void M() { var sb = new StringBuilder(); try { sb.Append($\"x{F()}\"); } finally { } }")]
    [InlineData("static void M() { var sb = Make(); sb.Append($\"x{F()}\"); }")]
    [InlineData("static void M() { StringBuilder sb; sb = new StringBuilder(); sb.Append($\"x{F()}\"); }")]
    [InlineData("sealed class B { static readonly StringBuilder Shared = new(); public static implicit operator StringBuilder(B b) => Shared; }\nstatic void M() { StringBuilder sb = new B(); sb.Append($\"x{F()}\"); }")]
    [InlineData("static StringBuilder f = new(); static void M() { f.Append($\"x{F()}\"); }")]
    [InlineData("static void M() { Make().Append($\"x{F()}\"); }")]
    [InlineData("static void M() { var c = new C(); c.Get().Append($\"x{F()}\"); }")]
    public void ABuilderOtherCodeMayReachKeepsTheHandler(string members) => AssertKept(members);

    /// <summary>A flow capture of any other object, made where a value branches, is not a handler.</summary>
    [Theory]
    [InlineData("static Version M(bool b) => b ? new Version(1, 2, 3) : null;")]
    [InlineData("static object M(bool b) => b ? new object() : null;")]
    public void AnyOtherCapturedCreationIsNotAHandler(string members)
    {
        IrProcedure procedure = Bound(members, LanguageVersion.CSharp10);

        Assert.Contains(Calls(procedure), static c => c.Callee.Value.EndsWith("::.ctor()", StringComparison.Ordinal) || c.Callee.Value.EndsWith("::.ctor(int,int,int)", StringComparison.Ordinal));
    }

    private static void AssertKept(string members) =>
        Assert.Contains(Calls(Bound(members, LanguageVersion.CSharp10)), static c => c.Callee.Value.StartsWith(Handler, StringComparison.Ordinal));

    private static IrProcedure Bound(string members, LanguageVersion version)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText($"using System;\nusing System.Text;\nclass C\n{{\n{Helpers}{members}\n}}\n", new CSharpParseOptions(version), cancellationToken: TestContext.Current.CancellationToken)],
            RoslynTestCompilations.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    /// <summary>
    /// The calls a run makes, each with its arguments, then how it ended. Every reference is element 1 of its sort and not
    /// null, except the builder when <paramref name="builderIsNull"/>; every integer is 3; each call answers as
    /// <see cref="IrGenOracle"/> does, a function of the callee and the arguments, so equal traces need equal arguments.
    /// </summary>
    private static string Trace(IrProcedure procedure, bool builderIsNull)
    {
        Recorder recorder = new();
        IrRun run = IrInterpreter.Run(procedure, new IrInputs([.. procedure.Parameters.Select(p => Input(p.Var, builderIsNull))]), recorder, IrGen.StepBudget);
        return $"{string.Join('\n', recorder.Calls)}\n{run.Outcome}";
    }

    private static IrValue Input(IrVar parameter, bool builderIsNull) => parameter.Type switch
    {
        IrSort sort => new IrSortValue(sort.Name, 1),
        IrBitVec bits => IrBitVecValue.FromSigned(bits.Width, 3),
        IrMap { Key: IrSort key, Value: IrBool } map => new IrMapValue(map, new IrBoolValue(Value: false), ImmutableDictionary<IrValue, IrValue>.Empty.Add(new IrSortValue(key.Name, 1), new IrBoolValue(builderIsNull && key.Name is "System.Text.StringBuilder"))),
        IrMap map => new IrMapValue(map, Input(new IrVar(parameter.Name, map.Value), builderIsNull), []),
        _ => new IrBoolValue(Value: false),
    };

    /// <summary>Answers as <see cref="IrGenOracle"/> does, and records each call with its arguments.</summary>
    private sealed class Recorder : ICallOracle
    {
        public List<string> Calls { get; } = [];

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Calls.Add($"{callee.Value}({string.Join(", ", arguments)})");
            return IrGenOracle.Instance.Answer(callee, arguments, resultType, position, heap, refOuts);
        }
    }
}
