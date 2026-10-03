using System.IO;

namespace Equiv.Samples.BclOverloadRebinding
{
    public static class Paths
    {
        public static string Strip(string s) => s.TrimEnd('/');

        public static int Indent(string s) => s.Length - s.TrimStart().Length;

        public static string Full(DirectoryInfo d) => d.FullName;

        public static string StripOther(string s) => s.TrimEnd('/');
    }
}
