using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

namespace Equiv.TestSupport;

/// <summary>
/// A deterministic call and pure oracle: the answer is an FNV-1a hash of the callee or function and the argument bits.
/// Supports Bool, bitvector and uninterpreted-sort results; about one call in eight "throws", and so does each exception
/// a pure function can raise.
/// </summary>
public sealed class IrGenOracle : ICallOracle, IPureOracle
{
    public static IrGenOracle Instance { get; } = new();

    /// <summary>Ignores <paramref name="position"/>: a stateless callee is one valid behaviour among many.</summary>
    public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position)
    {
        ArgumentNullException.ThrowIfNull(callee);
        ulong hash = Hash(callee.Value, arguments);
        return new IrCallResult(Value(hash, resultType), (hash >> 61) == 0);
    }

    public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(pure);
        ulong hash = Hash(pure.Function, arguments);
        return new IrPureResult(Value(hash, pure.Target.Type)!, [.. pure.Throws.Select((_, i) => ((hash >> (58 - (3 * i))) & 7) == 0)]);
    }

    private static ulong Hash(string function, ImmutableArray<IrValue> arguments)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in function)
        {
            hash = (hash ^ c) * 1099511628211;
        }

        foreach (IrValue argument in arguments)
        {
            hash = (hash ^ (argument is IrBitVecValue bits ? bits.Bits : (ulong)argument.GetHashCode())) * 1099511628211;
        }

        return hash;
    }

    private static IrValue? Value(ulong hash, IrType? resultType)
    {
        IrValue? value = resultType switch
        {
            null => null,
            IrBool => new IrBoolValue((hash & 1) != 0),
            IrBitVec bitVec => new IrBitVecValue(bitVec.Width, hash & (ulong.MaxValue >> (64 - bitVec.Width))),
            IrSort sort => new IrSortValue(sort.Name, (int)(hash & int.MaxValue)),
            _ => throw new NotSupportedException($"IrGenOracle cannot produce {resultType}."),
        };
        return value;
    }
}
