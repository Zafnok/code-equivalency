namespace Equiv.Samples.LoopBoundChange
{
    public class Summation
    {
        public int SumUpTo(int n)
        {
            int sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += i;
            }

            return sum;
        }
    }
}
