namespace Equiv.Core.Ir;

/// <summary>
/// The one definition of how a binary floating-point number is spelled in IR (VERIFICATION-MODEL.md section 2; ADR 0053
/// decision 5; ticket P1-030). <c>float</c> is the sort <see cref="Binary32"/> and <c>double</c> the sort
/// <see cref="Binary64"/>, uninterpreted like any other. A number is the element whose id is its IEEE 754 bits, binary32
/// zero-extended, with every NaN the one quiet NaN: a NaN's payload and sign are not observables. Equal numbers are
/// therefore equal elements, <c>+0</c> and <c>-0</c> are two, and NaN is one. An element a solver's model makes up in a
/// query that does not interpret the sort has an id of its own choosing, which is no number's bits by intent.
/// </summary>
public static class IrFloat
{
    /// <summary>The bits of the one binary32 NaN.</summary>
    private const uint NaN32 = 0x7FC0_0000;

    /// <summary>The bits of the one binary64 NaN.</summary>
    private const long NaN64 = 0x7FF8_0000_0000_0000;

    /// <summary>The sort of <c>float</c>, IEEE 754 binary32.</summary>
    public static IrSort Binary32 { get; } = new("System.Single");

    /// <summary>The sort of <c>double</c>, IEEE 754 binary64.</summary>
    public static IrSort Binary64 { get; } = new("System.Double");

    /// <summary>32 for <see cref="Binary32"/>, 64 for <see cref="Binary64"/>, and null for every other type.</summary>
    public static int? Width(IrType type) => type switch
    {
        IrSort sort when sort == Binary32 => 32,
        IrSort sort when sort == Binary64 => 64,
        _ => null,
    };

    /// <summary>The element <paramref name="value"/> is.</summary>
    public static IrSortValue Of(float value) => new(Binary32.Name, float.IsNaN(value) ? NaN32 : BitConverter.SingleToUInt32Bits(value));

    /// <summary>The element <paramref name="value"/> is.</summary>
    public static IrSortValue Of(double value) => new(Binary64.Name, double.IsNaN(value) ? NaN64 : BitConverter.DoubleToInt64Bits(value));

    /// <summary>The element whose IEEE bits are <paramref name="bits"/>, of the sort <paramref name="width"/> bits wide.</summary>
    public static IrSortValue OfBits(int width, ulong bits) =>
        width == 32 ? Of(BitConverter.UInt32BitsToSingle((uint)bits)) : Of(BitConverter.UInt64BitsToDouble(bits));

    /// <summary>The <c>float</c> whose bits <paramref name="value"/>'s id is.</summary>
    public static float ToSingle(IrSortValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return BitConverter.UInt32BitsToSingle((uint)value.Id);
    }

    /// <summary>The <c>double</c> whose bits <paramref name="value"/>'s id is.</summary>
    public static double ToDouble(IrSortValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return BitConverter.Int64BitsToDouble(value.Id);
    }
}
