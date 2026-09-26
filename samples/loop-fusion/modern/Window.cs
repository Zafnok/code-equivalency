namespace Equiv.Samples.LoopFusion;

public class Window
{
    // The two passes of the legacy method, fused into one loop.
    public int CountOutside(int count, int low, int high)
    {
        int inside = 0;
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            if (i >= low && i < high)
            {
                inside++;
            }

            total++;
        }

        return total - inside;
    }
}
