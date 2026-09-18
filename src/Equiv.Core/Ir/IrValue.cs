namespace Equiv.Core.Ir;

/// <summary>A runtime value of the interpreter and a constant operand of <see cref="IrConst"/> and <see cref="IrSwitch"/>.</summary>
public abstract record IrValue
{
    private protected IrValue()
    {
    }

    public abstract IrType Type { get; }
}
