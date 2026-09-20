using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

namespace Equiv.TestSupport;

/// <summary>
/// A deterministic call oracle: the answer is an FNV-1a hash of the callee and argument bits.
/// Supports Bool, bitvector and uninterpreted-sort results; about one call in eight "throws".
/// </summary>
public sealed class IrGenOracle : ICallOracle
{
    public static IrGenOracle Instance { get; } = new();

    public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType)
    {
        ArgumentNullException.ThrowIfNull(callee);
        ulong hash = 14695981039346656037;
        foreach (char c in callee.Value)
        {
            hash = (hash ^ c) * 1099511628211;
        }

        foreach (IrValue argument in arguments)
        {
            hash = (hash ^ (argument is IrBitVecValue bits ? bits.Bits : (ulong)argument.GetHashCode())) * 1099511628211;
        }

        IrValue? value = resultType switch
        {
            null => null,
            IrBool => new IrBoolValue((hash & 1) != 0),
            IrBitVec bitVec => new IrBitVecValue(bitVec.Width, hash & (ulong.MaxValue >> (64 - bitVec.Width))),
            IrSort sort => new IrSortValue(sort.Name, (int)(hash & int.MaxValue)),
            _ => throw new NotSupportedException($"IrGenOracle cannot produce {resultType}."),
        };
        return new IrCallResult(value, (hash >> 61) == 0);
    }
}
