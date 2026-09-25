using System.Linq;

namespace Equiv.Samples.ApiDrift
{
    public static class Text
    {
        public static string[] Parts(string s) => s.Split(',');

        public static bool HasX(string s) => s.Contains('x');
    }
}
