using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// Every observable agrees for every input (VERIFICATION-MODEL.md section 1). SARIF EQ001. <see cref="Method"/>
/// names the rung that proved it; <see cref="BoundedBy"/> is the unrolling bound when the proof is
/// <see cref="ProofMethod.Bounded"/> and a loop or self-recursion existed, and null otherwise. <see cref="Invariant"/> is
/// the coupling invariant a <see cref="ProofMethod.Chc"/>, <see cref="ProofMethod.LlmInvariant"/> or <see cref="ProofMethod.TraceInvariant"/> proof used, and null
/// otherwise (tickets P1-001, P1-002 and P1-009), SARIF <c>properties.invariant</c>. <see cref="ProposedBy"/> names what proposed
/// an admitted hypothesis (the model id of a <see cref="ProofMethod.LlmInvariant"/> proof, <c>trace</c> for a <see cref="ProofMethod.TraceInvariant"/> one), and is null when the checker
/// found the proof itself (ADR 0036), SARIF <c>properties.proposedBy</c>. <see cref="ContractsUsed"/> lists the callee
/// contracts the proof used in place of shared callee functions (ADR 0036 decision 2; ticket P1-010), empty for a proof that
/// used none; SARIF suffixes <c>proofMethod</c> with <c>+contract</c> when it is not empty.
/// </summary>
public sealed record Equivalent(ProofMethod Method, int? BoundedBy = null) : Verdict
{
    public string? Invariant { get; init; }

    public string? ProposedBy { get; init; }

    public ImmutableArray<ContractUse> ContractsUsed { get; init; } = [];

    // Deliberate non-short-circuit '&': see the comment on Equiv.Core.Configuration.EquivConfig.Equals.
    public bool Equals(Equivalent? other) =>
        other is not null
        && base.Equals(other)
        && (Method == other.Method)
            & (BoundedBy == other.BoundedBy) // NOSONAR
            & string.Equals(Invariant, other.Invariant, StringComparison.Ordinal) // NOSONAR
            & string.Equals(ProposedBy, other.ProposedBy, StringComparison.Ordinal) // NOSONAR
            & IrEquality.SequenceEqual(ContractsUsed, other.ContractsUsed); // NOSONAR

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Method, BoundedBy, Invariant, ProposedBy, IrEquality.Hash(ContractsUsed));
}
