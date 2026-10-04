namespace Equiv.Core.Verdicts;

/// <summary>
/// One rung the loop ladder attempted, its outcome and a human-readable detail. SARIF <c>properties.ladderTrace</c>.
/// <see cref="Mode"/> is the theory rung 4 ran in, when it ran (ticket P1-001), SARIF <c>properties.chcMode</c>.
/// <see cref="Solver"/> is the second solver that answered one of the rung's queries, when one did (ADR 0050 decision 4;
/// ticket P1-033): the step's <c>solver</c> in <c>ladderTrace</c>, and the <c>+cvc5</c> suffix of <c>proofMethod</c>.
/// </summary>
public sealed record LadderStep(ProofMethod Rung, RungOutcome Outcome, string Detail)
{
    public ChcMode? Mode { get; init; }

    public SolverUse? Solver { get; init; }
}
