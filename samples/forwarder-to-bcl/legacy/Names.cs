namespace Equiv.Samples.ForwarderToBcl
{
    public static class Text
    {
        public static bool Blank(string s) => string.IsNullOrWhiteSpace(s);

        public static bool BlankTrimmed(string s) => string.IsNullOrWhiteSpace(s?.Trim());
    }

    public static class Names
    {
        public static string Name(string s) => Text.Blank(s) ? "none" : s;

        public static string Trimmed(string s) => Text.BlankTrimmed(s) ? "none" : s;
    }
}
