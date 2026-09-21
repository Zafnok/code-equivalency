namespace Equiv.Core.Verdicts;

/// <summary>Why a verdict could not be reached (VERIFICATION-MODEL.md sections 1 and 6, EQ003).</summary>
public enum UnknownReason
{
    /// <summary>The solver did not finish within the configured timeout.</summary>
    Timeout,

    /// <summary>Some input reaches an <c>IrOpaque</c> node on either side (ADR 0014).</summary>
    Opaque,

    /// <summary>Matching could not choose among several equally-normalised overloads.</summary>
    UnmatchedOverload,

    /// <summary>A procedure has a back edge and no loop rung is wired yet (ticket M3-001; the M3-002 ladder replaces this).</summary>
    Loop,
}
