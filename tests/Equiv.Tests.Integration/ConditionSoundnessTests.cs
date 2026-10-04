using System.Reflection;

using CsCheck;

using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// The soundness property of ADR 0048 (ticket P1-022 criterion 6), on the pairs of the differential soundness gate
/// (ticket M0-012): for a pair whose verdict carries <c>agreesWhen</c>, every generated input that satisfies the condition
/// gives equal observables on the CLR. The condition is evaluated as the C# its <c>text</c> is, so a text that is not a
/// predicate over the modern side's parameters fails too. The pair count and the seed are the gate's
/// (<see cref="DifferentialSoundnessTests.Budget"/>): 200 pairs per pull request. The nightly job selects the gate's
/// class alone, so it does not run this one; with its budget set, this runs 5,000 pairs.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConditionSoundnessTests
{
    private const int InputsPerPair = 20;

    /// <summary>The legacy side of <see cref="ARemovedGuardAgreesWhereItsConditionHolds"/>: negative arguments give 0.</summary>
    private const string Guarded = """
        public static class Oracle
        {
            public static int F;

            public static int M(int a, int b, long c, long d, bool e, string s, int[] u)
            {
                if (a < 0)
                {
                    return 0;
                }

                return unchecked(a + 1);
            }
        }

        """;

    /// <summary>Its modern side: the guard dropped.</summary>
    private const string Unguarded = """
        public static class Oracle
        {
            public static int F;

            public static int M(int a, int b, long c, long d, bool e, string s, int[] u)
            {
                return unchecked(a + 1);
            }
        }

        """;

    private static readonly PairInput[] AroundZero =
    [
        new(-5, 0, 0, 0, E: false, SIsNull: false, 0, U: null),
        new(-1, 0, 0, 0, E: true, SIsNull: true, 7, [1, 2, 3]),
        new(0, 0, 0, 0, E: false, SIsNull: false, 0, U: null),
        new(41, 0, 0, 0, E: false, SIsNull: true, 0, U: null),
        new(int.MaxValue, 0, 0, 0, E: false, SIsNull: false, 0, U: null),
    ];

    /// <summary>No generated pair with a condition runs differently on an input its condition holds on.</summary>
    [Fact]
    public void GeneratedPairsAgreeWhereverTheirConditionHolds() =>
        Assert.Null(Record.Exception(static () => Gen.Select(DifferentialSoundnessTests.Generated, PairGen.Input.Array[InputsPerPair])
            .Sample(
                static c => Contradiction(c.Item1.LegacySource, c.Item1.ModernSource, c.Item2) is null,
                seed: DifferentialSoundnessTests.Seed,
                iter: DifferentialSoundnessTests.Pairs,
                print: static c => $"{Contradiction(c.Item1.LegacySource, c.Item1.ModernSource, c.Item2)}\nlegacy:\n{c.Item1.LegacySource}modern:\n{c.Item1.ModernSource}")));

    /// <summary>
    /// A pair the property is not vacuous on: the dropped guard is the legacy body's own predicate, so the pair is Divergent
    /// and proved Equivalent when the argument is not negative, and it runs alike on every such input.
    /// </summary>
    [Fact]
    public void ARemovedGuardAgreesWhereItsConditionHolds()
    {
        Divergent divergent = Assert.IsType<Divergent>(PairRuntime.Analyse(Guarded, Unguarded).Verdict);

        Assert.Equal("a >= 0", divergent.Conditions?.AgreesWhen?.Text);
        Assert.Null(Contradiction(Guarded, Unguarded, AroundZero));
    }

    /// <summary>A condition that is wrong is caught: the pair differs on every negative argument, where <c>a &lt; 0</c> holds.</summary>
    [Fact]
    public void AWrongConditionIsCaught()
    {
        string? contradiction = Contradiction(Guarded, Unguarded, AroundZero, condition: "a < 0");

        Assert.NotNull(contradiction);
        Assert.Contains("a < 0 holds on a=-5 ", contradiction, StringComparison.Ordinal);
        Assert.Contains("legacy observable: return 0 ", contradiction, StringComparison.Ordinal);
        Assert.Contains("modern observable: return -4 ", contradiction, StringComparison.Ordinal);
    }

    /// <summary>An input the condition throws on does not satisfy it.</summary>
    [Fact]
    public void AConditionThatThrowsHoldsNowhere() =>
        Assert.Null(Contradiction(Guarded, Unguarded, AroundZero, condition: "(a / (a - a)) < 0"));

    /// <summary>
    /// The first of <paramref name="inputs"/> on which <paramref name="condition"/> (by default the pair's own
    /// <c>agreesWhen.text</c>) holds and the two sides' observables differ, described; null when there is none, or the
    /// pair has no condition.
    /// </summary>
    private static string? Contradiction(string legacy, string modern, PairInput[] inputs, string? condition = null)
    {
        PairRuntime.Analysis analysis = PairRuntime.Analyse(legacy, modern);
        if ((condition ?? ConditionSearch.Of(analysis.Verdict)?.AgreesWhen?.Text) is not { } text)
        {
            return null;
        }

        using PairRuntime.Loaded loaded = new(analysis);
        MethodInfo holds = loaded.Condition(text);
        foreach (PairInput input in inputs.Where(input => PairRuntime.Loaded.Holds(holds, input)))
        {
            (string legacyObservable, string modernObservable) = loaded.Observe(input);
            if (!string.Equals(legacyObservable, modernObservable, StringComparison.Ordinal))
            {
                return $"{text} holds on {input}\nlegacy observable: {legacyObservable}\nmodern observable: {modernObservable}";
            }
        }

        return null;
    }
}
