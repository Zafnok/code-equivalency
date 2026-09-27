namespace Equiv.Core.Verdicts;

/// <summary>
/// Every observable agrees for every input (VERIFICATION-MODEL.md section 1). SARIF EQ001. <see cref="Method"/>
/// names the rung that proved it; <see cref="BoundedBy"/> is the unrolling bound when the proof is
/// <see cref="ProofMethod.Bounded"/> and a loop or self-recursion existed, and null otherwise. <see cref="Invariant"/> is
/// the coupling invariant a <see cref="ProofMethod.Chc"/> proof found, and null otherwise (ticket P1-001), SARIF
/// <c>properties.invariant</c>.
/// </summary>
public sealed record Equivalent(ProofMethod Method, int? BoundedBy = null) : Verdict
{
    public string? Invariant { get; init; }
}
