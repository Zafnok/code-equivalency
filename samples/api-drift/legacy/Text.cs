using System.Linq;

namespace Equiv.Samples.ApiDrift
{
    public static class Text
    {
        public static string[] Parts(string s) => s.Split(',');

        public static bool HasX(string s) => s.Contains('x');
    }

    public sealed class Tally
    {
        private int seen;

        public int Count(string s)
        {
            string[] parts = s.Split(',');
            int before = seen;
            Add(parts.Length);
            return seen - before;
        }

        public int Total(string s)
        {
            string[] parts = s.Split(',');
            int before = seen;
            Add(parts.Length);
            return seen - before;
        }

        private void Add(int n)
        {
            seen += n;
        }
    }
}
