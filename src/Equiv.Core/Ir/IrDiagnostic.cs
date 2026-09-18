namespace Equiv.Core.Ir;

/// <summary>One validator finding. <see cref="Id"/> is one of <see cref="IrDiagnosticIds"/>.</summary>
public sealed record IrDiagnostic(string Id, IrBlockId? Block, string Message);
