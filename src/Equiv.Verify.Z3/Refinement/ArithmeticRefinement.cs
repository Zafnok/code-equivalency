using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Refinement;

/// <summary>
/// Rung 1 on a product whose hard arithmetic is abstracted (<see cref="ArithmeticAbstraction"/>), refined by the models
/// that turn out to depend on it (ADR 0025 and ADR 0026, clarifications of 2026-10-07; ticket P1-031). After ARDiff
/// (Badihi, Akinotcho, Li and Rubin, "ARDiff: scaling program equivalence checking via iterative abstraction and
/// refinement of common code", ESEC/FSE 2020). Each round asks rung 1's queries beside the <see cref="Facts"/> so far:
/// <list type="bullet">
/// <item>an unsatisfiable query is unsatisfiable on the exact product too, since every real run is a run of this one;</item>
/// <item>a model that gives every application the real operator's value is a model of the exact product, and rung 1 reads
/// it as it reads any other;</item>
/// <item>any other model of the divergence query is replayed in <see cref="IrInterpreter"/>, which computes the real
/// arithmetic, and a replay that ends on both sides and diverges on an untainted observable is a counterexample;</item>
/// <item>otherwise the model is spurious, and each value it got wrong becomes a fact the next round assumes
/// (<see cref="ArithmeticAbstraction.Broken"/>).</item>
/// </list>
/// The facts are point facts, so a pair that needs an operator's algebra (<c>x * y</c> against <c>y * x</c>) never runs
/// out of spurious models; <see cref="MaxRounds"/> ends it, undecided.
/// </summary>
internal sealed class ArithmeticRefinement(Context context, ProductEncoding encoding, ArithmeticAbstraction arithmetic, VerificationOptions options)
{
    /// <summary>The most rounds a pair is asked.</summary>
    public const int MaxRounds = 8;

    private readonly List<BoolExpr> facts = [];

    /// <summary>What the rounds so far have learned of the real operators; every query of the next round assumes it.</summary>
    public IReadOnlyList<BoolExpr> Facts => facts;

    /// <summary>
    /// Runs <paramref name="round"/>, rung 1 on the abstracted product, until one adds no fact, at most
    /// <see cref="MaxRounds"/> times. Each is a step that names its round and carries the facts it added. A round that
    /// gave up is no cause of its own: a pair no round decides is the Unknown it was without them.
    /// </summary>
    public ImmutableArray<LoopLadder.Rung> Rounds(Func<LoopLadder.Rung> round)
    {
        ImmutableArray<LoopLadder.Rung>.Builder rounds = ImmutableArray.CreateBuilder<LoopLadder.Rung>();
        int added = 1;
        for (int number = 1; number <= MaxRounds && added > 0; number++)
        {
            int before = facts.Count;
            LoopLadder.Rung asked = round();
            added = facts.Count - before;
            string detail = string.Create(CultureInfo.InvariantCulture, $"arithmetic abstracted, round {number}: {asked.Step.Detail}");
            rounds.Add(asked with { Step = asked.Step with { Detail = detail, FactsAdded = added }, Cause = null });
        }

        return rounds.ToImmutable();
    }

    /// <summary>
    /// What a round makes of <paramref name="model"/> when it is not a model of the exact product, and null when it is one.
    /// Given <paramref name="replay"/>, the pair the divergence query's model is replayed through, a replay that diverges
    /// refutes the pair. Otherwise the values the model got wrong join <see cref="Facts"/> and the round is inconclusive.
    /// </summary>
    public LoopLadder.Rung? Spurious(SolverModel model, (IrProcedure Old, IrProcedure New)? replay)
    {
        ImmutableArray<BoolExpr> broken = arithmetic.Broken(model);
        if (broken.IsEmpty)
        {
            return null;
        }

        if (replay is { } pair && Stages.Timed(options, Stages.Replay, () => ModelDecoder.TryReplay(context, model, encoding, pair.Old, pair.New, LoopLadder.ReplayBudget)) is { } real)
        {
            return LoopLadder.Refuted(ProofMethod.Bounded, "the model's inputs diverge under the real operators", real);
        }

        facts.AddRange(broken);
        string count = broken.Length.ToString(CultureInfo.InvariantCulture);
        return new LoopLadder.Rung(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"the model is spurious; operator results it gets wrong: {count}"));
    }
}
