namespace Equiv.Core.Verdicts;

/// <summary>
/// Every observable agrees for every input (VERIFICATION-MODEL.md section 1). SARIF EQ001. <see cref="Method"/>
/// names the rung that proved it; <see cref="BoundedBy"/> is the unrolling bound when the proof is
/// <see cref="ProofMethod.Bounded"/> and a loop or self-recursion existed, and null otherwise. <see cref="Invariant"/> is
/// the coupling invariant a <see cref="ProofMethod.Chc"/>, <see cref="ProofMethod.LlmInvariant"/> or <see cref="ProofMethod.TraceInvariant"/> proof used, and null
/// otherwise (tickets P1-001, P1-002 and P1-009), SARIF <c>properties.invariant</c>. <see cref="ProposedBy"/> names what proposed
/// an admitted hypothesis (the model id of a <see cref="ProofMethod.LlmInvariant"/> proof, <c>trace</c> for a <see cref="ProofMethod.TraceInvariant"/> one), and is null when the checker
/// found the proof itself (ADR 0036), SARIF <c>properties.proposedBy</c>.
/// </summary>
public sealed record Equivalent(ProofMethod Method, int? BoundedBy = null) : Verdict
{
    public string? Invariant { get; init; }

    public string? ProposedBy { get; init; }
}
