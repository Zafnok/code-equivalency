using System.Collections.Immutable;

using CsCheck;

using Equiv.Core.Verdicts;
using Equiv.Corpus.Seeder;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// The differential soundness gate (VERIFICATION-MODEL.md section 7; ticket M0-012): generated C# pairs, executed on
/// the CLR, never contradict the verdict the real frontend and <see cref="Equiv.Verify.Z3.Z3Backend"/> give them. It
/// is EMI's metamorphic idea (Le, Afshari and Su, PLDI 2014) pointed at the checker. Each pair is run on
/// <see cref="InputsPerPair"/> CsCheck inputs and on its Divergent model, if it has one, and must satisfy:
/// <list type="number">
/// <item>soundness: if any input gives different observables, the verdict is not Equivalent;</item>
/// <item>decoding: if the verdict is Divergent, replaying its model in C# gives different observables;</item>
/// <item>precision: if the operator is preserving, the verdict is not Divergent.</item>
/// </list>
/// A failure prints the shrunk pair as its two classes, the operator, the input and both observables, which is a
/// ready-made regression test. <see cref="Budget"/> sets the pair count and the seed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DifferentialSoundnessTests
{
    private const int InputsPerPair = 20;

    /// <summary>
    /// Pairs per pull request and per nightly run, and the fixed seed (acceptance criterion 3). The nightly job sets
    /// <c>EQUIV_DIFFERENTIAL_BUDGET=nightly</c>; <c>EQUIV_DIFFERENTIAL_SEED</c> overrides the seed, to replay a failure.
    /// </summary>
    internal static readonly (int PullRequest, int Nightly, string Seed) Budget = (200, 5_000, "000000000000");

    /// <summary>
    /// Failures a rule tolerates until the P2 ticket named beside each is fixed (acceptance criterion 7), matched by a
    /// symptom in the failure's input line rather than by pair, so the nightly run skips every pair the bug shows in and
    /// not only the one it shrank to. Must be empty before M3-003 lands.
    /// </summary>
    private static readonly ImmutableArray<(string Symptom, string Ticket)> Skips = [];

    private static int Pairs =>
        string.Equals(Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_BUDGET"), "nightly", StringComparison.OrdinalIgnoreCase) ? Budget.Nightly : Budget.PullRequest;

    private static string Seed => Environment.GetEnvironmentVariable("EQUIV_DIFFERENTIAL_SEED") is { Length: > 0 } seed ? seed : Budget.Seed;

    /// <summary>Rule 1.</summary>
    [Fact]
    public void ObservedDivergenceIsNeverEquivalent() => Sample(Soundness);

    /// <summary>Rule 2.</summary>
    [Fact]
    public void DivergentModelReplaysAsDivergence() => Sample(Decoding);

    /// <summary>Rule 3.</summary>
    [Fact]
    public void PreservingMutationIsNeverDivergent() => Sample(Precision);

    private static void Sample(Func<Case, Failure?> rule) =>
        Gen.Select(PairGen.Pair, PairGen.Input.Array[InputsPerPair], static (pair, inputs) => new Case(pair.LegacySource, pair.ModernSource, pair.Operator, inputs))
            .Sample(c => rule(c) is not { } failure || Skipped(failure), seed: Seed, iter: Pairs, print: c => rule(c)?.Describe(c) ?? string.Empty);

    private static bool Skipped(Failure failure) => Skips.Any(s => failure.Input.Contains(s.Symptom, StringComparison.Ordinal));

    private static Failure? Soundness(Case c)
    {
        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern);
        if (analysis.Verdict is not Equivalent)
        {
            return null;
        }

        using PairRuntime.Loaded loaded = new(analysis);
        foreach (PairInput input in c.Inputs)
        {
            (string legacy, string modern) = loaded.Observe(input);
            if (!string.Equals(legacy, modern, StringComparison.Ordinal))
            {
                return new Failure(input.ToString(), legacy, modern);
            }
        }

        return null;
    }

    private static Failure? Decoding(Case c)
    {
        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern);
        if (analysis.Verdict is not Divergent)
        {
            return null;
        }

        if (analysis.Model is not { } model)
        {
            return new Failure(analysis.ModelProblem!, "-", "-");
        }

        using PairRuntime.Loaded loaded = new(analysis);
        (string legacy, string modern) = loaded.Observe(model);
        return string.Equals(legacy, modern, StringComparison.Ordinal) ? new Failure(model.ToString(), legacy, modern) : null;
    }

    private static Failure? Precision(Case c)
    {
        if (!PairGen.IsPreserving(c.Operator))
        {
            return null;
        }

        PairRuntime.Analysis analysis = PairRuntime.Analyse(c.Legacy, c.Modern);
        if (analysis.Verdict is not Divergent)
        {
            return null;
        }

        if (analysis.Model is not { } model)
        {
            return new Failure(analysis.ModelProblem!, "-", "-");
        }

        using PairRuntime.Loaded loaded = new(analysis);
        (string legacy, string modern) = loaded.Observe(model);
        return new Failure(model.ToString(), legacy, modern);
    }

    private sealed record Case(string Legacy, string Modern, MutationOperator Operator, PairInput[] Inputs)
    {
        public override string ToString() => $"{Operator}\n{Legacy}\n{Modern}";
    }

    /// <summary>What a rule saw go wrong on one input (or, for an undecodable model, why there is no input).</summary>
    private sealed record Failure(string Input, string LegacyObservable, string ModernObservable)
    {
        /// <summary>Acceptance criterion 4: both methods, the operator, the input and both observables, and nothing else.</summary>
        public string Describe(Case c) =>
            $"operator: {c.Operator}\ninput: {Input}\nlegacy observable: {LegacyObservable}\nmodern observable: {ModernObservable}\nlegacy:\n{c.Legacy}modern:\n{c.Modern}";
    }
}
