namespace Equiv.Core.Ir;

/// <summary>
/// One exception an <see cref="IrPure"/> function can raise: <paramref name="Flag"/> is a Bool output, true when it does,
/// and <paramref name="ExceptionType"/> the exception's exact type, which the frontend branches to (ADR 0025).
/// </summary>
public sealed record IrPureThrow(IrVar Flag, string ExceptionType);
