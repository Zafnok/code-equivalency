namespace Equiv.Core.Verdicts;

/// <summary>
/// <see cref="Reason"/> plus a human-readable <see cref="Detail"/> (the solver's message, the opaque
/// node's source span, the ambiguous overload's candidates, ...). SARIF EQ003.
/// </summary>
public sealed record Unknown(UnknownReason Reason, string Detail) : Verdict;
