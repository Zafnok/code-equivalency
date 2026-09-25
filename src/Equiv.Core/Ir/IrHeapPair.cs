namespace Equiv.Core.Ir;

/// <summary>
/// One heap map an <see cref="IrCall"/> reads and writes (VERIFICATION-MODEL.md section 2; ticket P1-005):
/// <paramref name="Map"/> is the name of the by-ref map parameter it versions, <paramref name="Before"/> the version the
/// call reads (a use) and <paramref name="After"/> the version it leaves (a definition).
/// </summary>
public sealed record IrHeapPair(string Map, IrVar Before, IrVar After);
