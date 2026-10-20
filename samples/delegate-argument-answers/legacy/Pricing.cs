using System;

namespace Equiv.Samples.DelegateArgumentAnswers
{
    public static class Pricing
    {
        public static int Total(Func<int, int> price, int count)
        {
            int unit = price(count);
            if (unit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(price));
            }

            return unit * count;
        }
    }
}
