namespace Equiv.Samples.RenamedLocals;

public class Calculator
{
    public int Add(int a, int b)
    {
        int total = a + b;
        return total;
    }

    public int Max(int a, int b)
    {
        int best = a;
        if (b > best)
        {
            best = b;
        }

        return best;
    }

    public int SumTo(int n)
    {
        int total = 0;
        for (int k = 0; k < n; k++)
        {
            total += k;
        }

        return total;
    }
}
