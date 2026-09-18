using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>
/// Answers opaque calls for <see cref="IrInterpreter"/>. Must be deterministic in
/// (<paramref name="callee"/>, arguments): the same question always gets the same answer.
/// </summary>
public interface ICallOracle
{
    /// <summary>
    /// Returns the call's result. <see cref="IrCallResult.Value"/> must be non-null and of
    /// type <paramref name="resultType"/> when <paramref name="resultType"/> is non-null.
    /// </summary>
    IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType);
}
