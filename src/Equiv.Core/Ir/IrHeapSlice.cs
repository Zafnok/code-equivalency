namespace Equiv.Core.Ir;

/// <summary>The value <paramref name="Value"/> of the heap map named <paramref name="Map"/> at a call (ticket P1-005).</summary>
public sealed record IrHeapSlice(string Map, IrValue Value);
