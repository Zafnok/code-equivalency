namespace Equiv.Core.Verdicts;

/// <summary>
/// <see cref="Reason"/> plus a human-readable <see cref="Detail"/> (the solver's message, the opaque
/// node's source span, the ambiguous overload's candidates, ...). SARIF EQ003.
/// </summary>
public sealed record Unknown(UnknownReason Reason, string Detail) : Verdict
{
    /// <summary>
    /// The <c>IrOpaque</c> reason a frontend gives a method whose bound body is erroneous; a pair with one on
    /// either side is <see cref="UnknownReason.Unbound"/> (ADR 0029 decision 2).
    /// </summary>
    public const string UnboundOpaqueReason = "unbound";
}
