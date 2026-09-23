namespace Equiv.Core.Verdicts;

/// <summary>One rung the loop ladder attempted, its outcome and a human-readable detail. SARIF <c>properties.ladderTrace</c>.</summary>
public sealed record LadderStep(ProofMethod Rung, RungOutcome Outcome, string Detail);
