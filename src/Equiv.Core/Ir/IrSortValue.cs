namespace Equiv.Core.Ir;

/// <summary>
/// An element of an uninterpreted sort. Elements compare by sort and id, never by reference. An element of
/// <see cref="IrFloat.Binary32"/> or <see cref="IrFloat.Binary64"/> that stands for a number has that number's IEEE bits as
/// its id (<see cref="IrFloat"/>; ADR 0053 decision 5), which is why an id is 64 bits wide.
/// </summary>
public sealed record IrSortValue(string Sort, long Id) : IrValue
{
    public override IrType Type => new IrSort(Sort);
}
