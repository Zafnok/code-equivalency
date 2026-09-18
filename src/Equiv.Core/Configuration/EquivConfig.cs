using System.Collections.Immutable;

namespace Equiv.Core.Configuration;

/// <summary>
/// Parsed and defaulted <c>equiv.config.json</c> (VERIFICATION-MODEL.md section 3; ARCHITECTURE.md's
/// <c>--bound</c>/<c>--timeout-ms</c> CLI defaults). <see cref="CallIdentityRenames"/> maps a legacy-side
/// <c>CallIdentity.Value</c> to its modern-side counterpart so matched calls unify (ticket M3-001).
/// </summary>
public sealed record EquivConfig(RenameMap Renames, ImmutableDictionary<string, string> CallIdentityRenames, int Bound, int TimeoutMs)
{
    public static EquivConfig Default { get; } = new(RenameMap.Empty, ImmutableDictionary<string, string>.Empty, Bound: 3, TimeoutMs: 5000);

    public bool Equals(EquivConfig? other) =>
        other is not null
        && (Renames == other.Renames)
            & ConfigEquality.DictionaryEqual(CallIdentityRenames, other.CallIdentityRenames)
            & (Bound == other.Bound)
            & (TimeoutMs == other.TimeoutMs);

    public override int GetHashCode() => HashCode.Combine(Renames, ConfigEquality.Hash(CallIdentityRenames), Bound, TimeoutMs);
}
