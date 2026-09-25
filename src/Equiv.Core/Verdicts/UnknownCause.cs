namespace Equiv.Core.Verdicts;

/// <summary>
/// A line an <see cref="Unknown"/> points at (ADR 0027 decision 4): a reached <c>IrOpaque</c> node or an abstraction the
/// result depends on, on one <see cref="Side"/>, with the <see cref="Reason"/> that becomes its SARIF
/// <c>relatedLocation</c> message.
/// </summary>
public sealed record UnknownCause(Codebase Side, string Reason, SourceSpan Span);
