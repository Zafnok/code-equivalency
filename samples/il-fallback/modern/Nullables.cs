namespace Equiv.Samples.IlFallback
{
    public static class Nullables
    {
        public static int? Add(int? a, int b) => a + b;

        public static int? Wrap(int x) => x;
    }
}
