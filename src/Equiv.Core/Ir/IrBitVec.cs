namespace Equiv.Core.Ir;

/// <summary>Fixed-width bitvector. Signedness lives on operations, never on the type.</summary>
public sealed record IrBitVec : IrType
{
    public IrBitVec(int width)
    {
        if (width is not (8 or 16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Bitvector width must be 8, 16, 32 or 64.");
        }

        Width = width;
    }

    public int Width { get; }
}
