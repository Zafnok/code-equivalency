using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Configuration;

/// <summary>
/// <see cref="Config"/> has every invalid or missing field replaced by its default, so a caller can
/// proceed after only warning; <see cref="Diagnostics"/> lists what was wrong, if anything.
/// </summary>
public sealed record EquivConfigResult(EquivConfig Config, ImmutableArray<EquivConfigDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.IsEmpty;

    public bool Equals(EquivConfigResult? other) =>
        other is not null
        && (Config == other.Config)
            & IrEquality.SequenceEqual(Diagnostics, other.Diagnostics);

    public override int GetHashCode() => HashCode.Combine(Config, IrEquality.Hash(Diagnostics));
}
