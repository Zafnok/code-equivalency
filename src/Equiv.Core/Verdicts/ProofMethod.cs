namespace Equiv.Core.Verdicts;

/// <summary>
/// A rung of the loop ladder (VERIFICATION-MODEL.md section 5.1; ADR 0008), or congruence (ADR 0024): how an
/// <see cref="Equivalent"/> was proved, and which rung a <see cref="LadderStep"/> ran. SARIF <c>properties.proofMethod</c>.
/// </summary>
public enum ProofMethod
{
    /// <summary>Rung 1: loops unrolled and self-recursion inlined k times. A proof only when no input reaches the bound.</summary>
    Bounded,

    /// <summary>Rung 2: lockstep relational induction over aligned loops (unbounded).</summary>
    LockstepInduction,

    /// <summary>Rung 3: rung 2 with k earlier iterations assumed equal (unbounded).</summary>
    KInduction,

    /// <summary>
    /// Rung 4: constrained Horn clauses solved by Z3 Spacer, which synthesises the coupling invariant itself; loops need not
    /// align (unbounded; ticket P1-001).
    /// </summary>
    Chc,

    /// <summary>
    /// Rung 5: a coupling invariant a language model proposed, admitted only once Z3 proves every rung 4 clause with it
    /// (unbounded; ticket P1-002, ADR 0036). The proposer is <see cref="Equivalent.ProposedBy"/>.
    /// </summary>
    LlmInvariant,

    /// <summary>
    /// Rung 5 with a local proposer: a coupling invariant mined from runs of both sides in <c>IrInterpreter</c>, admitted
    /// exactly as <see cref="LlmInvariant"/> is (unbounded; ticket P1-009, ADR 0036). <see cref="Equivalent.ProposedBy"/> is
    /// <c>trace</c>.
    /// </summary>
    TraceInvariant,

    /// <summary>
    /// No rung: the two bound bodies fingerprint equal and neither is runtime-sensitive, so the solver is not called (ADR 0024;
    /// ticket M3-015). Unbounded, and modular like every verdict (ADR 0019).
    /// </summary>
    Congruence,
}
