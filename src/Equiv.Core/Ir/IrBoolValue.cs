namespace Equiv.Core.Ir;

public sealed record IrBoolValue(bool Value) : IrValue
{
    public override IrType Type => new IrBool();
}
