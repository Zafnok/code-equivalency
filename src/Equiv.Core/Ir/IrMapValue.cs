using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// An SMT array value: <see cref="Default"/> everywhere except <see cref="Entries"/>. Equality is
/// extensional, so an entry equal to the default is the same as no entry.
/// </summary>
public sealed record IrMapValue(IrMap MapType, IrValue Default, ImmutableDictionary<IrValue, IrValue> Entries) : IrValue
{
    public override IrType Type => MapType;

    public IrValue Read(IrValue key) => Entries.GetValueOrDefault(key, Default);

    public IrMapValue Write(IrValue key, IrValue value) => this with { Entries = Entries.SetItem(key, value) };

    public bool Equals(IrMapValue? other) =>
        other is not null
        && (MapType == other.MapType) & (Default == other.Default) & Agrees(this, other) & Agrees(other, this);

    public override int GetHashCode() =>
        HashCode.Combine(
            MapType,
            Default,
            Entries.Where(e => e.Value != Default).Aggregate(0, static (hash, e) => hash ^ HashCode.Combine(e.Key, e.Value)));

    private static bool Agrees(IrMapValue left, IrMapValue right) => left.Entries.All(e => right.Read(e.Key) == e.Value);
}
