using CsCheck;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.TestSupport;
using Equiv.Verify.Z3;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// An unchanged lambda end to end (ADR 0024 decision 2; ticket M4-004 criteria 6 and 7): the real frontend and
/// <see cref="Z3Backend"/> decide a method whose unchanged lambda sits beside a changed integer branch, and, over generated
/// pairs whose change lies outside the lambda or inside it, never call two programs Equivalent that the CLR runs
/// differently. Since ticket P2-067 the lambda is no opaque fragment but the pure function <c>delegate:&lt;fingerprint&gt;</c>,
/// shared by both sides when their fingerprints are equal; the fragments that stay opaque are held to the same property by
/// <see cref="DifferentialSoundnessTests"/>.
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
    /// integer guard <c>cap &lt; 1</c> on one side and <c>cap &lt;= 0</c> on the other. The lambda is one shared function, and
    /// neither side holds an opaque, so the method is decided.
    /// </summary>
    [Fact]
    public void BusinessLayerCappedLineCountSharesItsLambdaAndIsEquivalent()
    {
        ProcedurePair pair = new CSharpFrontend().Analyze(Solution("legacy", "*.sln"), Solution("modern", "*.slnx"), EquivConfig.Default, NullRunLog.Instance, TestContext.Current.CancellationToken)
            .Match.Pairs.Single(static p => p.New.Value.Contains("OrderService::CappedLineCount(", StringComparison.Ordinal));
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        Assert.Empty(Opaques(pair.OldBody!));
        Assert.Empty(Opaques(pair.NewBody!));
        Assert.Equal(Assert.Single(Delegates(pair.OldBody!)), Assert.Single(Delegates(pair.NewBody!)), StringComparer.Ordinal);
        Assert.IsType<Equivalent>(new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, options));
    }

    /// <summary>
    /// Criterion 7, the soundness property of VERIFICATION-MODEL.md section 7 over <see cref="PairGen.FragmentPair"/>: a pair
    /// the backend calls Equivalent gives the same observables on every input. At least one such pair shares its lambda's
    /// function, so the property is not vacuous.
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

                if (Delegates(analysis.Old).Overlaps(Delegates(analysis.New)))
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

        Assert.True(sharedAndEquivalent > 0, "no generated pair shared a lambda's function and was Equivalent");
    }

    /// <summary>
    /// Ticket P2-067: a lambda that differs between the sides is two functions, one per side, so a pair that hands it to a
    /// call is Unknown(Abstraction) naming both, with the candidate input, where an unshared fragment made it
    /// Unknown(Opaque). Two lambdas that are bound alike are one function, and the pair is Equivalent.
    /// </summary>
    [Fact]
    public void ALambdaThatDiffersIsUnknownAbstractionNamingADelegateOnEachSide()
    {
        Unknown unknown = Assert.IsType<Unknown>(PairRuntime.Analyse(Applying("v => unchecked(v + a)"), Applying("v => unchecked(v - a)")).Verdict);

        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.NotNull(unknown.Candidate);
        Assert.Equal(
            [Codebase.Legacy, Codebase.Modern],
            unknown.Abstractions.Where(static a => a.Identity.Value.StartsWith("delegate:", StringComparison.Ordinal)).Select(static a => a.Side).Order());
        Assert.IsType<Equivalent>(PairRuntime.Analyse(Applying("v => unchecked(v + a)"), Applying("w => unchecked(w + a)")).Verdict);
    }

    /// <summary>A method that applies <paramref name="lambda"/>, which captures <c>a</c>, to <c>b</c>.</summary>
    private static string Applying(string lambda) => $$"""
        public static class Oracle
        {
            public static int F;

            public static int M(int a, int b, long c, long d, bool e, string s, int[] u)
            {
                int x = ((System.Func<int, int>)({{lambda}}))(b);
                return x;
            }
        }

        """;

    private static IEnumerable<IrOpaque> Opaques(IrProcedure procedure) =>
        procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>();

    /// <summary>The <c>delegate:</c> functions <paramref name="procedure"/> applies.</summary>
    private static HashSet<string> Delegates(IrProcedure procedure) =>
        new(
            procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function).Where(static f => f.StartsWith("delegate:", StringComparison.Ordinal)),
            StringComparer.Ordinal);

    private static string Solution(string side, string pattern) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", side), pattern).Single();
}
