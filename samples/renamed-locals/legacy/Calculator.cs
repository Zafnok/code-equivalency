namespace Equiv.Samples.RenamedLocals
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            int sum = a + b;
            return sum;
        }

        public int Max(int a, int b)
        {
            int result = a;
            if (b > result)
            {
                result = b;
            }

            return result;
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
}
