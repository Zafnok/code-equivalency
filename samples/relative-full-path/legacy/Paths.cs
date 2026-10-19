using System.IO;

namespace Equiv.Samples.RelativeFullPath
{
    public static class Paths
    {
        public static string Full(string relative)
        {
            return Path.GetFullPath(relative);
        }
    }
}
