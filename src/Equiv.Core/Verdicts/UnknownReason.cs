namespace Equiv.Core.Verdicts;

/// <summary>Why a verdict could not be reached (VERIFICATION-MODEL.md sections 1, 5.1 and 6, EQ003).</summary>
public enum UnknownReason
{
    /// <summary>The solver did not finish within the configured timeout.</summary>
    Timeout,

    /// <summary>Some input reaches an <c>IrOpaque</c> node on either side (ADR 0014).</summary>
    Opaque,

    /// <summary>Matching could not choose among several equally-normalised overloads.</summary>
    UnmatchedOverload,

    /// <summary>
    /// Loops the ladder's rungs 1 to 3 could not decide: they do not align (loop count, nesting or header state
    /// differs), or they align and neither induction rung proved them (ticket M3-002; rung 4 is P1-001).
    /// </summary>
    UnalignedLoop,

    /// <summary>A self-recursive procedure the ladder could not decide (ticket M3-002).</summary>
    Recursion,

    /// <summary>
    /// A side's bound body is erroneous: a compiler error, an invalid operation or an error-type symbol (ADR 0029
    /// decision 2). The frontend lowers it to an <c>IrOpaque</c> with reason <see cref="Unknown.UnboundOpaqueReason"/>.
    /// </summary>
    Unbound,

    /// <summary>
    /// The solver found a divergence, but its replay differs only in observables that depend on an abstraction, which
    /// the solver may interpret in a way no real run does (ADR 0026). The result carries the candidate counterexample.
    /// </summary>
    Abstraction,
}
