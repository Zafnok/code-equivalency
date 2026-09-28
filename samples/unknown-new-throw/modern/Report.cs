using System;

namespace Equiv.Samples.UnknownNewThrow
{
    public static class Report
    {
        public static int Width(int count)
        {
            string digits = $"{count}";
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return digits.Length;
        }
    }
}
