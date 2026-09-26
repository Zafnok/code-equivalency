namespace Equiv.Samples.LoopToLinq;

public class Readings
{
    // values.Count(v => v > 0), written out: a call to Enumerable.Count would be a callee the verifier cannot see
    // into, so this is the loop LINQ runs, with its enumerator's pre-incremented index.
    public int CountPositive(int[] values)
    {
        int count = 0;
        int index = -1;
        while (++index < values.Length)
        {
            if (values[index] > 0)
            {
                count++;
            }
        }

        return count;
    }
}
