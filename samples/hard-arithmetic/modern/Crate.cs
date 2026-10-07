namespace Equiv.Samples.HardArithmetic;

public class Crate
{
    public long Volume(bool metric, long width, long widthInches, long height, long depth)
    {
        if (metric)
        {
            return width * height * depth;
        }

        long inches = widthInches;
        return inches * height * depth;
    }

    public long Weight(bool metric, long width, long widthInches, long height, long density)
    {
        if (metric)
        {
            return width * height * density;
        }

        long inches = widthInches;
        return inches * height * height;
    }
}
