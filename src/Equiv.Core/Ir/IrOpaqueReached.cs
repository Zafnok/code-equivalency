namespace Equiv.Core.Ir;

/// <summary>Execution reached an <see cref="IrOpaque"/>; nothing after it is meaningful.</summary>
public sealed record IrOpaqueReached(string Reason, SourceSpan Span) : IrOutcome;
