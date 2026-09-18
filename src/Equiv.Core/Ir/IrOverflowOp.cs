namespace Equiv.Core.Ir;

/// <summary>Checked operations whose overflow <see cref="IrOverflows"/> tests; one Z3 predicate each.</summary>
public enum IrOverflowOp
{
    SAdd,
    UAdd,
    SSub,
    USub,
    SMul,
    UMul,
    SDiv,
}
