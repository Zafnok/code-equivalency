namespace Equiv.Core.Ir;

/// <summary>Names the SSA version <paramref name="Final"/> of by-ref parameter <paramref name="Param"/> live at an exit.</summary>
public sealed record IrOut(IrVar Param, IrVar Final);
