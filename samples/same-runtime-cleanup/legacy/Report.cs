namespace Equiv.Samples.SameRuntimeCleanup;

public class Report
{
    public string Describe(double amount, string label)
    {
        int whole = (int)amount;
        if (label.StartsWith("net"))
        {
            return amount.ToString();
        }

        return whole.ToString();
    }

    public int Rank(int score)
    {
        if (score == 0)
        {
            return 10;
        }

        if (score == 1)
        {
            return 20;
        }

        return 0;
    }
}
