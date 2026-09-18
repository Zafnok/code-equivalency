namespace Equiv.Core.Ir;

/// <summary>SMT array from <paramref name="Key"/> to <paramref name="Value"/>; one per field or array, in SSA.</summary>
public sealed record IrMap(IrType Key, IrType Value) : IrType;
