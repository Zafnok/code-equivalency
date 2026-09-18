namespace Equiv.Core.Ir;

/// <summary>What an <see cref="ICallOracle"/> answers for one call.</summary>
public sealed record IrCallResult(IrValue? Value, bool Threw);
