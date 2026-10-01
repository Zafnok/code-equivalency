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
        return score switch
        {
            0 => 10,
            1 => 20,
            _ => 0,
        };
    }
}
