namespace Equiv.Core.Ir;

/// <summary>Uninterpreted sort with equality only (strings, objects, decimals, floats).</summary>
public sealed record IrSort(string Name) : IrType;
