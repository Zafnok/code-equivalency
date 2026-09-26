using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M4-006 end to end: an <c>await</c> is a call on its awaitable, keyed by its trace position (ADR 0018), so
/// unchanged async code is provable, a throw is the same observable on both async sides, and two awaits of one task are
/// never forced to agree. These need the frontend together with the solver, which is why they live here.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AwaitEquivalenceTests
{
    private const string Tasks = "System.Threading.Tasks";

    private static readonly ImmutableArray<MetadataReference> References =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    /// <summary>ADR 0018: the second await is a call at another position, so its result is free of the first's.</summary>
    [Fact]
    public void TwoAwaitsOfTheSameTaskAreNotForcedEqual()
    {
        Verdict verdict = Verify(
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) => await t - await t; }}",
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ await t; await t; return 0; }} }}");

        Assert.IsNotType<Equivalent>(verdict);
    }

    [Fact]
    public void AnExtraAwaitIsNotEquivalent()
    {
        Verdict verdict = Verify(
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) => await t; }}",
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ await t; return await t; }} }}");

        Assert.IsNotType<Equivalent>(verdict);
    }

    [Theory]
    [InlineData(
        $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t, int k) {{ int x = await t.ConfigureAwait(false); return x + k; }} }}",
        $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t, int k) {{ var pending = t.ConfigureAwait(false); int sum = await pending; sum += k; return sum; }} }}")]
    [InlineData(
        $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ int x = await t; if (x == 0) throw new InvalidOperationException(); return 10 / x; }} }}",
        $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ int x = await t; if (x != 0) return 10 / x; throw new InvalidOperationException(); }} }}")]
    public void UnchangedAsyncCodeIsEquivalent(string legacy, string modern) =>
        Assert.IsType<Equivalent>(Verify(legacy, modern));

    /// <summary>Criterion 1: a throw in an async method is the observable a synchronous throw is, so its type counts.</summary>
    [Fact]
    public void AThrowOfAnotherTypeIsNotEquivalent()
    {
        Verdict verdict = Verify(
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ int x = await t; if (x == 0) throw new InvalidOperationException(); return x; }} }}",
            $"class C {{ static async {Tasks}.Task<int> M({Tasks}.Task<int> t) {{ int x = await t; if (x == 0) throw new ArgumentException(); return x; }} }}");

        Assert.IsType<Divergent>(verdict);
    }

    /// <summary>Criterion 5: <c>ConfirmAsync</c> lowers with no opaque on either side.</summary>
    [Fact]
    public void BusinessLayerConfirmAsyncHasNoOpaque()
    {
        ProcedurePair confirm = new CSharpFrontend().Analyze(Solution("legacy", "*.sln"), Solution("modern", "*.slnx"), EquivConfig.Default, TestContext.Current.CancellationToken)
            .Match.Pairs.Single(static p => p.New.Value.Contains("OrderService::ConfirmAsync(", StringComparison.Ordinal));

        foreach (IrProcedure body in (IrProcedure[])[confirm.OldBody!, confirm.NewBody!])
        {
            Assert.DoesNotContain(body.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
            Assert.Contains(body.Blocks.SelectMany(static b => b.Instructions), static i => i is IrCall c && c.Callee.Value.StartsWith("await:", StringComparison.Ordinal));
        }

        Assert.IsType<Equivalent>(new Z3Backend().Verify(confirm.OldBody!, confirm.NewBody!, Options));
    }

    private static Verdict Verify(string legacy, string modern) =>
        new Z3Backend().Verify(Lower(legacy, isLegacy: true), Lower(modern, isLegacy: false), Options);

    private static IrProcedure Lower(string source, bool isLegacy)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText("using System;\n" + source, cancellationToken: TestContext.Current.CancellationToken)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        IrProcedure procedure = CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, isLegacy).Body;
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    private static string Solution(string side, string pattern) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", side), pattern).Single();
}
