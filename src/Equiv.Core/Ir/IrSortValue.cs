namespace Equiv.Core.Ir;

/// <summary>An element of an uninterpreted sort. Elements compare by sort and id, never by reference.</summary>
public sealed record IrSortValue(string Sort, int Id) : IrValue
{
    public override IrType Type => new IrSort(Sort);
}
