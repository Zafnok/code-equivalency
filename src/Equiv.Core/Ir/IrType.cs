namespace Equiv.Core.Ir;

/// <summary>IR type lattice (VERIFICATION-MODEL.md section 2). Closed: only the records below derive.</summary>
public abstract record IrType
{
    private protected IrType()
    {
    }
}
