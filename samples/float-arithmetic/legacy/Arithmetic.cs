namespace Equiv.Samples.FloatArithmetic
{
    public static class Arithmetic
    {
        public static double Twice(double a)
        {
            return a * 2.0;
        }

        public static double Combine(double a, double b)
        {
            return a + b;
        }

        public static double Sum(double a, double b, double c)
        {
            return (a + b) + c;
        }

        public static bool IsOrdered(float low, float high)
        {
            return low <= high;
        }

        public static Celsius Mean(double a, double b)
        {
            return (a + b) / 2.0;
        }
    }
}
