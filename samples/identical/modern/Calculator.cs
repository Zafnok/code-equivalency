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

    public int SumTo(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += i;
        }

        return sum;
    }
}
