using System;

namespace Equiv.Samples.RuntimeRowUnaffectedOverload
{
    public static class Text
    {
        public static bool Has(string s) => s.IndexOf("x.exe", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasCulture(string s) => s.IndexOf("x.exe", StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
