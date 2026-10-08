using System.Collections.Frozen;
using System.Globalization;

using Equiv.Core.ApiEquivalences;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// What the frontend knows, at a call, about the value of an integer argument (ADR 0020, clarified for ticket P2-142): a
/// compile-time constant is its value, and any other argument is some value of its type. An API-equivalence adapter
/// item with an <see cref="ApiIntegerRange"/> addresses a call only when all of that lies inside the range, so the
/// range is shown at the call and never assumed.
/// </summary>
internal static class KnownIntegers
{
    /// <summary>Every value of each integral type a <c>long</c> holds; <c>ulong</c>, <c>char</c> and the native integers are left out.</summary>
    private static readonly FrozenDictionary<SpecialType, (long Least, long Most)> Types = new Dictionary<SpecialType, (long Least, long Most)>
    {
        [SpecialType.System_SByte] = (sbyte.MinValue, sbyte.MaxValue),
        [SpecialType.System_Byte] = (byte.MinValue, byte.MaxValue),
        [SpecialType.System_Int16] = (short.MinValue, short.MaxValue),
        [SpecialType.System_UInt16] = (ushort.MinValue, ushort.MaxValue),
        [SpecialType.System_Int32] = (int.MinValue, int.MaxValue),
        [SpecialType.System_UInt32] = (uint.MinValue, uint.MaxValue),
        [SpecialType.System_Int64] = (long.MinValue, long.MaxValue),
    }.ToFrozenDictionary();

    /// <summary>
    /// Whether <paramref name="operand"/> is an integer that is known to lie in <paramref name="range"/> and whose type
    /// converts implicitly to a signed integer of the range's width: every value of the type fits that width, which a
    /// constant of a wider type does not make so (<c>5L</c> does not convert to <c>int</c>).
    /// </summary>
    public static bool IsWithin(IOperation operand, ApiIntegerRange range)
    {
        int spare = 64 - range.Bits;
        if (operand.Type is not { } type
            || !Types.TryGetValue(type.SpecialType, out (long Least, long Most) all)
            || all.Least < long.MinValue >> spare
            || all.Most > long.MaxValue >> spare)
        {
            return false;
        }

        (long least, long most) = operand.ConstantValue.HasValue ? Constant(operand.ConstantValue.Value) : all;
        return least >= range.Min && most <= range.Max;
    }

    private static (long Least, long Most) Constant(object? value)
    {
        long constant = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return (constant, constant);
    }
}
