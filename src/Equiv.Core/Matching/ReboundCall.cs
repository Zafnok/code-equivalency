namespace Equiv.Core.Matching;

/// <summary>
/// Two callee identities a matched pair treats as possibly the same function (ADR 0042; ticket P2-069): a call site with
/// the same source text on both sides binds to <paramref name="Legacy"/> on the legacy side and to
/// <paramref name="Modern"/> on the modern side. Every call to either is an <c>IrOpaque</c> with reason
/// <see cref="OpaqueReason"/>, so an input that reaches one has an unknown outcome (ADR 0014). SARIF
/// <c>properties.reboundCalls</c>.
/// </summary>
public sealed record ReboundCall(string Legacy, string Modern)
{
    /// <summary>The <c>IrOpaque</c> reason of a call to an identity in a rebound pair.</summary>
    public const string OpaqueReason = "rebound-call";
}
