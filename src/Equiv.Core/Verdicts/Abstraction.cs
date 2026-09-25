namespace Equiv.Core.Verdicts;

/// <summary>
/// An abstraction a candidate counterexample depends on (ADR 0026): a call the replay taints, on one
/// <see cref="Side"/>. <see cref="Span"/> is its source span when the IR records one; an <c>IrCall</c> carries none, so
/// until shared fragments (ticket M4-004) emit <c>opaque:</c> calls from spanned <c>IrOpaque</c> nodes it is null.
/// SARIF <c>properties.abstractions</c>.
/// </summary>
public sealed record Abstraction(Codebase Side, CallIdentity Identity, SourceSpan? Span)
{
    /// <summary>The line this abstraction points an <see cref="Unknown"/> at, when it has a span.</summary>
    public UnknownCause? Cause => Span is null ? null : new UnknownCause(Side, $"abstraction {Identity.Value}", Span);
}
