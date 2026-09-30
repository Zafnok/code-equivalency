namespace Equiv.Samples.IlFallback
{
    public static class Nullables
    {
        public static int? Add(int? a, int b)
        {
            if (a.HasValue)
            {
                return new int?(a.GetValueOrDefault() + b);
            }

            return null;
        }

        public static int? Wrap(int x) => new int?(x);
    }
}
