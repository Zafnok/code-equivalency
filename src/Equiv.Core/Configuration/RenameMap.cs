using System.Collections.Immutable;

namespace Equiv.Core.Configuration;

/// <summary>
/// Namespace and type rename maps from <c>equiv.config.json</c> (VERIFICATION-MODEL.md section 3):
/// a legacy-side namespace, or a legacy-side fully-qualified type name, maps to its modern-side
/// counterpart so both sides normalise to the same <see cref="ProcedureIdentity"/>.
/// </summary>
public sealed record RenameMap(ImmutableDictionary<string, string> Namespaces, ImmutableDictionary<string, string> Types)
{
    public static RenameMap Empty { get; } = new([], []);

    // Deliberate non-short-circuit '&': see the comment on EquivConfig.Equals.
    public bool Equals(RenameMap? other) =>
        other is not null
        && ConfigEquality.DictionaryEqual(Namespaces, other.Namespaces)
            & ConfigEquality.DictionaryEqual(Types, other.Types); // NOSONAR

    public override int GetHashCode() => HashCode.Combine(ConfigEquality.Hash(Namespaces), ConfigEquality.Hash(Types));
}
