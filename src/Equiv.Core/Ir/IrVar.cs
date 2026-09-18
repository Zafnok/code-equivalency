namespace Equiv.Core.Ir;

/// <summary>An SSA variable. <paramref name="SourceName"/> is the source local or parameter name when known.</summary>
public sealed record IrVar(string Name, IrType Type, string? SourceName = null);
