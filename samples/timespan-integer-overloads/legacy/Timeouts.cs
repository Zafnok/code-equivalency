using System;

namespace Equiv.Samples.TimeSpanIntegerOverloads
{
    public static class Timeouts
    {
        private const int CacheHours = 12;

        public static TimeSpan Week() => TimeSpan.FromDays(7);

        public static TimeSpan CacheLifetime() => TimeSpan.FromHours(CacheHours);

        public static TimeSpan Retry() => TimeSpan.FromMinutes(5);

        public static TimeSpan Poll() => TimeSpan.FromSeconds(30);

        public static TimeSpan Never() => TimeSpan.FromMilliseconds(-1);

        public static TimeSpan Wait(int seconds) => TimeSpan.FromSeconds(seconds);

        public static TimeSpan Backoff() => TimeSpan.FromMinutes(1);

        public static TimeSpan TooLong() => TimeSpan.FromHours(256204779);

        public static TimeSpan Hours(int hours) => TimeSpan.FromHours(hours);

        public static TimeSpan Delay(long milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
    }
}
