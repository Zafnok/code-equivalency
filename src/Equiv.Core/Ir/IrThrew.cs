namespace Equiv.Core.Ir;

public sealed record IrThrew(string ExceptionType) : IrOutcome;
