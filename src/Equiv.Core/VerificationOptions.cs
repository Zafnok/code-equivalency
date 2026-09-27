using System.Collections.Immutable;

using Equiv.Core.Configuration;

namespace Equiv.Core;

/// <summary>
/// Per-run knobs for <see cref="IVerificationBackend"/> (ticket M3-001): the loop bound, the solver timeout, and the
/// call-identity unifications from <c>equiv.config.json</c>. <see cref="ChcIntMode"/> (<c>--chc-int-mode</c>, ticket
/// P1-001) lets rung 4 of the loop ladder encode bitvectors as integers once it has proved that sound; it is on unless
/// turned off. <see cref="InvariantModel"/> (<c>--invariant-model</c>, ticket P1-002) is the model rung 5 asks for a
/// coupling invariant when rung 4 times out; rung 5 is skipped when it is null, the default.
/// </summary>
public sealed record VerificationOptions(int Bound, int TimeoutMs, ImmutableDictionary<string, string> CallIdentityMap)
{
    public bool ChcIntMode { get; init; } = true;

    public string? InvariantModel { get; init; }

    // Deliberate non-short-circuit '&': see the comment on Equiv.Core.Configuration.EquivConfig.Equals.
    public bool Equals(VerificationOptions? other) =>
        other is not null
        && (Bound == other.Bound)
            & (TimeoutMs == other.TimeoutMs) // NOSONAR
            & ConfigEquality.DictionaryEqual(CallIdentityMap, other.CallIdentityMap) // NOSONAR
            & (ChcIntMode == other.ChcIntMode) // NOSONAR
            & string.Equals(InvariantModel, other.InvariantModel, StringComparison.Ordinal); // NOSONAR

    public override int GetHashCode() => HashCode.Combine(Bound, TimeoutMs, ConfigEquality.Hash(CallIdentityMap), ChcIntMode, InvariantModel);
}
