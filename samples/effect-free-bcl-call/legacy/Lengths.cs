using System.Collections.Generic;

namespace Equiv.Samples.EffectFreeBclCall
{
    public static class Lengths
    {
        public static int First(string a, string b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            return a.Length;
        }

        public static int Reset(string a, List<int> list)
        {
            if (a == null)
            {
                return 0;
            }

            return a.Length;
        }
    }
}
