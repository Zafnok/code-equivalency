namespace Equiv.Core.Verdicts;

/// <summary>Why a verdict could not be reached (VERIFICATION-MODEL.md sections 1 and 6, EQ003).</summary>
public enum UnknownReason
{
    /// <summary>The solver did not finish within the configured timeout.</summary>
    Timeout,

    /// <summary>An <c>IrOpaque</c> node reaches an observable.</summary>
    Opaque,

    /// <summary>Matching could not choose among several equally-normalised overloads.</summary>
    UnmatchedOverload,
}
