using System;

namespace Equiv.Samples.FrameworkStaticFields
{
    public static class Audit
    {
        public static int Count;

        public static void Record(string text)
        {
            Count++;
        }
    }

    public static class Labels
    {
        public static string Describe(string name)
        {
            string fallback = string.Empty;
            Audit.Record(name);
            return name ?? fallback;
        }

        public static string Trimmed(string name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            return name.Trim();
        }

        public static IntPtr Release(string name)
        {
            IntPtr none = IntPtr.Zero;
            Audit.Record(name);
            return none;
        }

        public static TimeSpan Forever(string name)
        {
            TimeSpan longest = TimeSpan.MaxValue;
            Audit.Record(name);
            return longest;
        }

        public static string Blank(string name)
        {
            Audit.Record(name);
            return string.Empty;
        }
    }
}
