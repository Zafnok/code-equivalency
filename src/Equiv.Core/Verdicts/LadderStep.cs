namespace Equiv.Core.Verdicts;

/// <summary>
/// One rung the loop ladder attempted, its outcome and a human-readable detail. SARIF <c>properties.ladderTrace</c>.
/// <see cref="Mode"/> is the theory rung 4 ran in, when it ran (ticket P1-001), SARIF <c>properties.chcMode</c>.
/// <see cref="Solver"/> is the second solver that answered one of the rung's queries, when one did (ADR 0050 decision 4;
/// ticket P1-033): the step's <c>solver</c> in <c>ladderTrace</c>, and the <c>+cvc5</c> suffix of <c>proofMethod</c>.
/// <see cref="FactsAdded"/> is set on a round of rung 1 asked with its hard arithmetic abstracted (ADR 0025, clarification
/// of 2026-10-07; ticket P1-031): how many facts about the real operators the round's model made the next round assume,
/// 0 for a round that ended the refinement. It is the step's <c>factsAdded</c> in <c>ladderTrace</c>, and a round that
/// proved or refuted the pair gives <c>proofMethod</c> its <c>+abstracted</c> suffix.
/// </summary>
public sealed record LadderStep(ProofMethod Rung, RungOutcome Outcome, string Detail)
{
    public ChcMode? Mode { get; init; }

    public SolverUse? Solver { get; init; }

    public int? FactsAdded { get; init; }

    /// <summary>Whether the step is an abstracted round that proved or refuted the pair.</summary>
    public bool DecidedAbstracted => FactsAdded is not null && Outcome is RungOutcome.Proved or RungOutcome.Refuted;
}
