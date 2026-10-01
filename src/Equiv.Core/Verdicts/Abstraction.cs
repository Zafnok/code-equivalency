namespace Equiv.Core.Verdicts;

/// <summary>
/// An abstraction a candidate counterexample depends on (ADR 0026): a call the replay taints, on one
/// <see cref="Side"/>. <see cref="Span"/> is its source span when the IR records one; an <c>IrCall</c> carries none, so
/// until shared fragments (ticket M4-004) emit <c>opaque:</c> calls from spanned <c>IrOpaque</c> nodes it is null.
/// <see cref="Reason"/> is the <c>IrOpaque</c> reason of the fragment an <c>opaque:</c> identity stands for (ticket P2-062),
/// and null for an <c>IrPure</c> operator. SARIF <c>properties.abstractions</c>.
/// </summary>
public sealed record Abstraction(Codebase Side, CallIdentity Identity, SourceSpan? Span)
{
    public string? Reason { get; init; }


    /// <summary>The line this abstraction points an <see cref="Unknown"/> at, when it has a span.</summary>
    public UnknownCause? Cause => Span is null ? null : new UnknownCause(Side, $"abstraction {Identity.Value}", Span);
}
