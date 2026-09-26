using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>
/// Answers opaque calls for <see cref="IrInterpreter"/>. Must be deterministic in
/// (callee, arguments, position, heap): the same question always gets the same answer. Two calls with
/// equal arguments at different positions may get different answers, because a real callee may be
/// stateful (ADR 0018).
/// </summary>
public interface ICallOracle
{
    /// <summary>
    /// Returns the call's result. <see cref="IrCallResult.Value"/> must be non-null and of
    /// type <paramref name="resultType"/> when <paramref name="resultType"/> is non-null.
    /// <paramref name="position"/> is the number of calls the run has made before this one. <paramref name="heap"/> is
    /// the value of each heap map the call reads and writes (ticket P1-005); <see cref="IrCallResult.Heap"/> is either
    /// empty, leaving every one unchanged, or one value of the same type per entry, in order. <paramref name="refOuts"/> is
    /// the type of each <c>ref</c> or <c>out</c> argument the call writes, in parameter order (ticket M4-003);
    /// <see cref="IrCallResult.RefOuts"/> is one value of that type per entry.
    /// </summary>
    IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts);
}
