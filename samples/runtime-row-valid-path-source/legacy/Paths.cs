namespace Equiv.Samples.RuntimeRowValidPathSource
{
    public static class Paths
    {
        public static string BaseDir() => System.IO.Path.GetDirectoryName(typeof(Paths).Assembly.Location);

        public static string Load(string p) => System.IO.File.Exists(p) ? new System.IO.StreamReader(p).ReadToEnd() : null;

        public static string Under(string root) => System.IO.Path.Combine(root, "Images");
    }
}
