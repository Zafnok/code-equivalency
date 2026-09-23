namespace Equiv.Core.Verdicts;

/// <summary>
/// A rung of the loop ladder (VERIFICATION-MODEL.md section 5.1; ADR 0008): how an <see cref="Equivalent"/>
/// was proved, and which rung a <see cref="LadderStep"/> ran. SARIF <c>properties.proofMethod</c>.
/// </summary>
public enum ProofMethod
{
    /// <summary>Rung 1: loops unrolled and self-recursion inlined k times. A proof only when no input reaches the bound.</summary>
    Bounded,

    /// <summary>Rung 2: lockstep relational induction over aligned loops (unbounded).</summary>
    LockstepInduction,

    /// <summary>Rung 3: rung 2 with k earlier iterations assumed equal (unbounded).</summary>
    KInduction,
}
