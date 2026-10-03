namespace Equiv.Core.Matching;

/// <summary>
/// A forwarder a matched pair's bodies call, and what each call to it was lowered as a call to (ADR 0047; ticket P2-068):
/// <paramref name="Forwarder"/> is the identity of a static method whose body is one call passing its own parameters
/// through, and <paramref name="Target"/> the callee identity at the end of its chain. SARIF
/// <c>properties.forwardersResolved</c>.
/// </summary>
public sealed record ResolvedForwarder(string Forwarder, string Target);
