using System.Collections.Immutable;
using System.Text.RegularExpressions;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// M2-003 against the real samples: every matched pair's bodies are lowered and validate (acceptance
/// criterion 1), and every <see cref="OperationKind"/> the samples contain has a row in
/// <c>docs/tickets/IOPERATION-COVERAGE.md</c> (acceptance criterion 4).
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class SampleLoweringTests
{
    /// <summary>The five M1-001 samples: every procedure their READMEs list is expected-Equivalent or expected-Divergent.</summary>
    private static readonly string[] Lowered = ["identical", "renamed-locals", "added-branch", "removed-null-check", "loop-bound-change"];

    private static readonly string[] Samples = [.. Lowered, "added-removed"];

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    public static TheoryData<string> SampleNames => [.. Samples];

    public static TheoryData<string> LoweredSampleNames => [.. Lowered];

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void EveryMatchedPairLowersToValidIr(string sample)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;

        Assert.NotEmpty(result.Pairs);
        foreach (ProcedurePair pair in result.Pairs)
        {
            Assert.Empty(IrValidator.Validate(pair.OldBody!));
            Assert.Empty(IrValidator.Validate(pair.NewBody!));
        }
    }

    /// <summary>
    /// Ticket M2-004 acceptance criterion 8: the five M1-001 samples lower with zero <see cref="IrOpaque"/>
    /// nodes. Every procedure they match is one their README marks expected-Equivalent or expected-Divergent.
    /// </summary>
    [Theory]
    [MemberData(nameof(LoweredSampleNames))]
    public void EveryMatchedPairLowersWithoutOpaqueNodes(string sample)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;

        Assert.NotEmpty(result.Pairs);
        foreach (IrProcedure body in result.Pairs.SelectMany(static p => new[] { p.OldBody!, p.NewBody! }))
        {
            Assert.Empty(body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Select(o => $"{body.Identity.Value}: {o.Reason}"));
        }
    }

    [Fact]
    public async Task EveryOperationKindInTheSamplesHasACoverageRow()
    {
        HashSet<OperationKind> kinds = [];
        foreach (string sample in Samples)
        {
            foreach (string side in (string[])["legacy", "modern"])
            {
                LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(Solution(sample, side), TestContext.Current.CancellationToken);
                kinds.UnionWith(loaded.Compilations.SelectMany(compilation => Kinds(compilation, TestContext.Current.CancellationToken)));
            }
        }

        string table = await File.ReadAllTextAsync(Path.Combine(RepoRoot, "docs", "tickets", "IOPERATION-COVERAGE.md"), TestContext.Current.CancellationToken);
        ImmutableHashSet<string> rows = [.. CoverageRow.Matches(table).Select(static m => m.Groups["kind"].Value)];
        ImmutableArray<string> missing = [.. kinds.Select(static k => k.ToString()).Where(k => !rows.Contains(k)).Order(StringComparer.Ordinal)];
        Assert.True(missing.IsEmpty, $"No IOPERATION-COVERAGE.md row for: {string.Join(", ", missing)}");
    }

    /// <summary>Kinds in each procedure's operation tree plus the CFG-only kinds (flow captures) its CFG introduces.</summary>
    private static IEnumerable<OperationKind> Kinds(Compilation compilation, CancellationToken cancellationToken) =>
        ProcedureEnumerator.Enumerate(compilation)
            .Select(p => p.Symbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken))
            .Select(syntax => compilation.GetSemanticModel(syntax.SyntaxTree).GetOperation(syntax, cancellationToken))
            .OfType<IOperation>()
            .SelectMany(body => body.DescendantsAndSelf().Concat(Cfg(body, cancellationToken)))
            .Select(static o => o.Kind);

    private static IEnumerable<IOperation> Cfg(IOperation body, CancellationToken cancellationToken) =>
        (body switch
        {
            IMethodBodyOperation method => ControlFlowGraph.Create(method, cancellationToken),
            IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor, cancellationToken), // lowered since ticket M4-001
            _ => null,
        })?.Blocks
            .SelectMany(static b => b.Operations.Concat(b.BranchValue is null ? [] : [b.BranchValue]))
            .SelectMany(static o => o.DescendantsAndSelf())
        ?? [];

    private static string Solution(string sample, string side) =>
        Directory.GetFiles(Path.Combine(RepoRoot, "samples", sample, side), string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();

    [GeneratedRegex(@"^\| (?<kind>\w+) \| (lowered|opaque|n/a)", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CoverageRow { get; }
}
