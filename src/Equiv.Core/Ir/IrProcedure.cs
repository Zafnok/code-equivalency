using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>A procedure: signature, ordered blocks and entry block (VERIFICATION-MODEL.md section 2).</summary>
public sealed record IrProcedure(
    ProcedureIdentity Identity,
    ImmutableArray<IrParameter> Parameters,
    IrType? ReturnType,
    ImmutableArray<IrBlock> Blocks,
    IrBlockId Entry)
{
    public bool Equals(IrProcedure? other) =>
        other is not null
        && (Identity == other.Identity)
            & (ReturnType == other.ReturnType)
            & (Entry == other.Entry)
            & IrEquality.SequenceEqual(Parameters, other.Parameters)
            & IrEquality.SequenceEqual(Blocks, other.Blocks);

    public override int GetHashCode() =>
        HashCode.Combine(Identity, ReturnType, Entry, IrEquality.Hash(Parameters), IrEquality.Hash(Blocks));
}
