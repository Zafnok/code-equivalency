namespace Equiv.Samples.Identical;

public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b;
    }

    public int Max(int a, int b)
    {
        if (a > b)
        {
            return a;
        }

        return b;
    }
}
