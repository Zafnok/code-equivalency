using System;

namespace Equiv.Samples.BclReboundOverloads
{
    public static class Calls
    {
        public static string TrimTail(string s) => s.TrimEnd();

        public static string TrimTailOther(string s) => s.TrimEnd();

        public static TimeSpan Hours(int hours) => TimeSpan.FromHours(hours);
    }
}
