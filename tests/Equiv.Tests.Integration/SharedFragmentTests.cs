using CsCheck;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.TestSupport;
using Equiv.Verify.Z3;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Shared opaque fragments end to end (ADR 0024 decision 2; ticket M4-004 criteria 6 and 7): the real frontend and
/// <see cref="Z3Backend"/> decide a method whose unchanged lambda sits beside a changed integer branch, and, over generated
/// pairs whose change lies outside a shared fragment or inside one, never call two programs Equivalent that the CLR runs
/// differently.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SharedFragmentTests
{
    private const int Pairs = 100;

    private const int InputsPerPair = 20;

    private const string Seed = "000000000000";

    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    /// <summary>
    /// Criterion 6: <c>OrderService.CappedLineCount</c> counts lines with the same lambda on both sides and spells its
    /// integer guard <c>cap &lt; 1</c> on one side and <c>cap &lt;= 0</c> on the other. The lambda is one shared call, so the
    /// method is decided.
    /// </summary>
    [Fact]
    public void BusinessLayerCappedLineCountSharesItsLambdaAndIsEquivalent()
    {
        ProcedurePair pair = new CSharpFrontend().Analyze(Solution("legacy", "*.sln"), Solution("modern", "*.slnx"), EquivConfig.Default, TestContext.Current.CancellationToken)
            .Match.Pairs.Single(static p => p.New.Value.Contains("OrderService::CappedLineCount(", StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        string? legacy = Assert.Single(Fragments(pair.OldBody!)).Fingerprint;
        string? modern = Assert.Single(Fragments(pair.NewBody!)).Fingerprint;
        Assert.NotNull(legacy);
        Assert.Equal(legacy, modern, StringComparer.Ordinal);
        Assert.IsType<Equivalent>(new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, options));
    }

    /// <summary>
    /// Criterion 7, the soundness property of VERIFICATION-MODEL.md section 7 over <see cref="PairGen.FragmentPair"/>: a pair
    /// the backend calls Equivalent gives the same observables on every input. At least one such pair shares a fragment,
    /// so the property is not vacuous.
    /// </summary>
    [Fact]
    public void AFragmentPairIsNeverEquivalentWhenItsProgramsDiffer()
    {
        int sharedAndEquivalent = 0;
        Gen.Select(PairGen.FragmentPair, PairGen.Input.Array[InputsPerPair]).Sample(
            sample =>
            {
                ((string legacy, string modern, _), PairInput[] inputs) = sample;
                PairRuntime.Analysis analysis = PairRuntime.Analyse(legacy, modern);
                if (analysis.Verdict is not Equivalent)
                {
                    return;
                }

                if (Fingerprints(analysis.Old).Overlaps(Fingerprints(analysis.New)))
                {
                    Interlocked.Increment(ref sharedAndEquivalent);
                }

                using PairRuntime.Loaded loaded = new(analysis);
                Assert.All(inputs, input =>
                {
                    (string old, string @new) = loaded.Observe(input);
                    Assert.Equal(old, @new);
                });
            },
            seed: Seed,
            iter: Pairs,
            threads: 1,
            print: static sample => $"{sample.Item1.Operator}\n{sample.Item1.LegacySource}\n{sample.Item1.ModernSource}");

        Assert.True(sharedAndEquivalent > 0, "no generated pair shared a fragment and was Equivalent");
    }

    private static IEnumerable<IrOpaque> Fragments(IrProcedure procedure) =>
        procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>();

    private static HashSet<string> Fingerprints(IrProcedure procedure) =>
        new(Fragments(procedure).Select(static o => o.Fingerprint).OfType<string>(), StringComparer.Ordinal);

    private static string Solution(string side, string pattern) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", side), pattern).Single();
}
