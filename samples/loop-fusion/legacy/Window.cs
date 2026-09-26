namespace Equiv.Samples.LoopFusion
{
    public class Window
    {
        public int CountOutside(int count, int low, int high)
        {
            int inside = 0;
            for (int i = 0; i < count; i++)
            {
                if (i >= low && i < high)
                {
                    inside++;
                }
            }

            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total++;
            }

            return total - inside;
        }
    }
}
