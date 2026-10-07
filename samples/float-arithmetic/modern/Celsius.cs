namespace Equiv.Samples.FloatArithmetic;

public struct Celsius
{
    public readonly double Degrees;

    public Celsius(double degrees)
    {
        Degrees = degrees;
    }

    public static implicit operator Celsius(double degrees)
    {
        return new Celsius(degrees);
    }
}
