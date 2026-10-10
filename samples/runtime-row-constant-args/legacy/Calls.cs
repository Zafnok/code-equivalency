namespace Equiv.Samples.RuntimeRowConstantArgs
{
    public static class Calls
    {
        public static string[] Words(string s) => System.Text.RegularExpressions.Regex.Split(s, @"\s+");

        public static string Cache() => System.IO.Path.Combine("C:\\data", "Images");

        public static string[] Letters(string s) => System.Text.RegularExpressions.Regex.Split(s, "(?i)[a-z]");

        public static string Piped() => System.IO.Path.Combine("a|b", "Images");

        public static string[] SplitBy(string s, string pattern) => System.Text.RegularExpressions.Regex.Split(s, pattern);

        public static string Under(string root) => System.IO.Path.Combine(root, "Images");
    }
}
