using Equiv.Core.Ir;

namespace Equiv.TestSupport;

/// <summary>A procedure that breaks exactly one validator rule, <paramref name="ExpectedId"/>.</summary>
public sealed record IrViolation(IrProcedure Procedure, string ExpectedId);
