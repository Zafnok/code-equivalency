namespace Equiv.Core.Ir;

/// <summary>
/// Binary operations. Add, Sub and Mul have no signed variant because wrapping arithmetic is
/// identical at the bit level. Bool operands: And, Or, Xor, Eq, Ne. Sort operands: Eq, Ne.
/// Division by zero and shifts by at least the width follow SMT-LIB bitvector semantics.
/// </summary>
public enum IrBinaryOp
{
    Add,
    Sub,
    Mul,
    SDiv,
    SRem,
    UDiv,
    URem,
    And,
    Or,
    Xor,
    Shl,
    AShr,
    LShr,
    Eq,
    Ne,
    Slt,
    Sle,
    Sgt,
    Sge,
    Ult,
    Ule,
    Ugt,
    Uge,
}
