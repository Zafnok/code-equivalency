namespace Equiv.Core.Ir;

/// <summary>A procedure parameter: an SSA input plus its passing kind.</summary>
public sealed record IrParameter(IrVar Var, IrParameterKind Kind);
