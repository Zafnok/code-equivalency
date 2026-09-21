using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>
/// Answers opaque calls for <see cref="IrInterpreter"/>. Must be deterministic in
/// (callee, arguments, position): the same question always gets the same answer. Two calls with
/// equal arguments at different positions may get different answers, because a real callee may be
/// stateful (ADR 0018).
/// </summary>
public interface ICallOracle
{
    /// <summary>
    /// Returns the call's result. <see cref="IrCallResult.Value"/> must be non-null and of
    /// type <paramref name="resultType"/> when <paramref name="resultType"/> is non-null.
    /// <paramref name="position"/> is the number of calls the run has made before this one.
    /// </summary>
    IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position);
}
