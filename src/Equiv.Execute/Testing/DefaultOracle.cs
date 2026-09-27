using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

namespace Equiv.Execute.Testing;

/// <summary>
/// Answers every call and pure function with its type's default value and no exception (ticket P1-008). The IR path
/// signature stops at the first branch on such an answer, so the answer only has to be deterministic.
/// </summary>
internal sealed class DefaultOracle : ICallOracle, IPureOracle
{
    public static DefaultOracle Instance { get; } = new();

    public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts) =>
        new(resultType is null ? null : Default(resultType), Threw: false) { RefOuts = [.. refOuts.Select(Default)] };

    public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments) =>
        new(Default(pure.Target.Type), [.. pure.Throws.Select(static _ => false)]);

    /// <summary><c>false</c>, zero, a sort's element 0, or a map holding its value type's default everywhere.</summary>
    public static IrValue Default(IrType type) => type switch
    {
        IrBool => new IrBoolValue(Value: false),
        IrBitVec bitVec => new IrBitVecValue(bitVec.Width, 0),
        IrSort sort => new IrSortValue(sort.Name, 0),
        _ => new IrMapValue((IrMap)type, Default(((IrMap)type).Value), []),
    };
}
