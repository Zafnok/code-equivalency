using System.Collections.Immutable;

using Equiv.Core.Configuration;

namespace Equiv.Core;

/// <summary>Per-run knobs for <see cref="IVerificationBackend"/> (ticket M3-001): the loop bound, the solver timeout, and the call-identity unifications from <c>equiv.config.json</c>.</summary>
public sealed record VerificationOptions(int Bound, int TimeoutMs, ImmutableDictionary<string, string> CallIdentityMap)
{
    public bool Equals(VerificationOptions? other) =>
        other is not null
        && (Bound == other.Bound)
            & (TimeoutMs == other.TimeoutMs)
            & ConfigEquality.DictionaryEqual(CallIdentityMap, other.CallIdentityMap);

    public override int GetHashCode() => HashCode.Combine(Bound, TimeoutMs, ConfigEquality.Hash(CallIdentityMap));
}
