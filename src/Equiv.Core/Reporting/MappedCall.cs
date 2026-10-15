namespace Equiv.Core.Reporting;

/// <summary>
/// One entry of the config's call-identity map that a pair's legacy body calls (ADR 0059; ticket P2-192): every legacy call
/// to <paramref name="Legacy"/> is the same function as a modern call to <paramref name="Modern"/>, which the user declared
/// and nothing in the run proves. SARIF <c>properties.callIdentitiesMapped</c>.
/// </summary>
public sealed record MappedCall(string Legacy, string Modern);
