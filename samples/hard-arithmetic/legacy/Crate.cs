namespace Equiv.Samples.HardArithmetic;

public class Crate
{
    public long Volume(bool metric, long width, long widthInches, long height, long depth)
    {
        long side = metric ? width : widthInches;
        return side * height * depth;
    }

    public long Weight(bool metric, long width, long widthInches, long height, long density)
    {
        long side = metric ? width : widthInches;
        return side * height * density;
    }
}
