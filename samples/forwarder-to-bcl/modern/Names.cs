namespace Equiv.Samples.ForwarderToBcl
{
    public static class Names
    {
        public static string Name(string s) => string.IsNullOrWhiteSpace(s) ? "none" : s;

        public static string Trimmed(string s) => string.IsNullOrWhiteSpace(s) ? "none" : s;
    }
}
