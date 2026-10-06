using System.Globalization;
using System.Text;

namespace Equiv.Verify.Z3.Tests;

/// <summary>IR text whose unrolling grows with the power of its nesting depth (ticket P1-032).</summary>
internal static class DeepLoops
{
    /// <summary>
    /// <paramref name="depth"/> loops, each nested in the one before, around a body of one block. Unrolled <c>k</c>
    /// times it holds on the order of <c>k</c> to the power <paramref name="depth"/> blocks.
    /// </summary>
    public static string Nested(int depth)
    {
        StringBuilder text = new("proc \"T::Deep(bool)\" (%c: bool) entry B0\nB0:\n  goto B1\n");
        for (int i = 1; i <= depth; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"B{i}:\n  br %c, B{i + 1}, B{depth + 1 + i}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"B{depth + 1}:\n  goto B{depth}\nB{depth + 2}:\n  ret\n");
        for (int i = 2; i <= depth; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"B{depth + 1 + i}:\n  goto B{i - 1}\n");
        }

        return text.ToString();
    }
}
