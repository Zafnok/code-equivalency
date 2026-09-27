namespace Equiv.Core.Verdicts;

/// <summary>
/// One rung the loop ladder attempted, its outcome and a human-readable detail. SARIF <c>properties.ladderTrace</c>.
/// <see cref="Mode"/> is the theory rung 4 ran in, when it ran (ticket P1-001), SARIF <c>properties.chcMode</c>.
/// </summary>
public sealed record LadderStep(ProofMethod Rung, RungOutcome Outcome, string Detail)
{
    public ChcMode? Mode { get; init; }
}
