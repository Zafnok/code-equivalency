namespace Equiv.Samples.FloatArithmetic;

public static class Arithmetic
{
    public static double Twice(double a) => a + a;

    public static double Combine(double a, double b) => a - b;

    public static double Sum(double a, double b, double c) => a + (b + c);

    public static bool IsOrdered(float low, float high) => high >= low;

    public static Celsius Mean(double a, double b) => (b + a) / 2.0;
}
