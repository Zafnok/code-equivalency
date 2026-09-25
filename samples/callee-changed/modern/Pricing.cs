namespace Equiv.Samples.CalleeChanged
{
    public static class Pricing
    {
        public static int Total(int a) => Tax(a) + a;

        public static int Tax(int a) => a / 5;
    }
}
