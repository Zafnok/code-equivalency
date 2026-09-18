namespace Equiv.Core.Ir;

/// <summary>A bitvector as its unsigned magnitude <see cref="Bits"/>; it has no sign of its own.</summary>
public sealed record IrBitVecValue : IrValue
{
    public IrBitVecValue(int width, ulong bits)
    {
        IrBitVec type = new(width);
        if ((bits & ~IrBits.Mask(width)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bits), bits, $"Value does not fit in {width} bits.");
        }

        BitVecType = type;
        Bits = bits;
    }

    public int Width => BitVecType.Width;

    public ulong Bits { get; }

    /// <summary>The two's-complement reading of <see cref="Bits"/>.</summary>
    public long TwosComplement => IrBits.ToSigned(Bits, Width);

    public override IrType Type => BitVecType;

    private IrBitVec BitVecType { get; }

    /// <summary>Wraps <paramref name="value"/> to <paramref name="width"/> bits.</summary>
    public static IrBitVecValue FromSigned(int width, long value) => new(width, unchecked((ulong)value) & IrBits.Mask(width));
}
