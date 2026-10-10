namespace Equiv.Samples.IlFallback
{
    public static class Nullables
    {
        public static int? Add(int? a, int b) => a + b;

        public static long? Wrap(int x) => x;
    }
}
