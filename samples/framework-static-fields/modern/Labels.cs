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
            Audit.Record(name);
            return name ?? string.Empty;
        }

        public static string Trimmed(string name)
        {
            if (name == null)
            {
                return "";
            }

            return name.Trim();
        }

        public static IntPtr Release(string name)
        {
            Audit.Record(name);
            return IntPtr.Zero;
        }

        public static TimeSpan Forever(string name)
        {
            Audit.Record(name);
            return TimeSpan.MaxValue;
        }

        public static string Blank(string name)
        {
            Audit.Record(name);
            return " ";
        }
    }
}
