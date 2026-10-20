using System;

namespace Equiv.Samples.DelegateArgumentAnswers;

public static class Pricing
{
    public static int Total(Func<int, int> price, int count)
    {
        int unit = price(count);
        return unit * count;
    }
}
