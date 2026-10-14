namespace Equiv.Samples.RuntimeRowUnreachableChange
{
    public static class Calls
    {
        public static string Cache() => System.IO.Path.Combine("C:\\data", "Images");

        public static int Slash(string s) => s.IndexOf('/');

        public static string[] Fields(string s) => s.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);

        public static string Under(string root) => System.IO.Path.Combine(root, "Images");

        public static int Find(string s, string t) => s.IndexOf(t);

        public static string[] FieldsBy(string s, System.StringSplitOptions options) => s.Split(new[] { ',' }, options);
    }
}
