namespace Equiv.Samples.RuntimeRowValidPathSource
{
    public static class Paths
    {
        public static string BaseDir() => System.IO.Path.GetDirectoryName(typeof(Paths).Assembly.Location);

        public static string Load(string p) => System.IO.File.Exists(p) ? new System.IO.StreamReader(p).ReadToEnd() : null;

        public static string Under(string root) => System.IO.Path.Combine(root, "Images");

        public static string Full(string p)
        {
            var full = System.IO.Path.GetFullPath(p);
            return full.Length > 1 && full[full.Length - 1] == System.IO.Path.DirectorySeparatorChar ? full.Substring(0, full.Length - 1) : full;
        }

        public static void Create(string p) => System.IO.Directory.CreateDirectory(Full(p));
    }
}
