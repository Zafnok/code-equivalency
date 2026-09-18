namespace Equiv.Core.Ir;

/// <summary>Execution reached an <see cref="IrUnreachable"/>: the input violates an assumption.</summary>
public sealed record IrInfeasible : IrOutcome;
