namespace Equiv.Core.Verdicts;

/// <summary>
/// Every observable agrees for every input (VERIFICATION-MODEL.md section 1). SARIF EQ001. <see cref="Method"/>
/// names the rung that proved it; <see cref="BoundedBy"/> is the unrolling bound when the proof is
/// <see cref="ProofMethod.Bounded"/> and a loop or self-recursion existed, and null otherwise.
/// </summary>
public sealed record Equivalent(ProofMethod Method, int? BoundedBy = null) : Verdict;
