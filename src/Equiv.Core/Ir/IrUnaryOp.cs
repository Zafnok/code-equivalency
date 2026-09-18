namespace Equiv.Core.Ir;

/// <summary>Unary operations. ZExt, SExt and Trunc take the target width from the target variable's type.</summary>
public enum IrUnaryOp
{
    Neg,
    Not,
    BoolNot,
    ZExt,
    SExt,
    Trunc,
}
