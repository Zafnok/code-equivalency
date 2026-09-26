namespace Equiv.Samples.LoopToLinq
{
    public class Readings
    {
        public int CountPositive(int[] values)
        {
            int count = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
