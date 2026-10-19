using System.Management;

namespace Equiv.Samples.PackageRuntimeAsset
{
    public static class Wmi
    {
        public static string Query(string className)
        {
            return new SelectQuery(className).QueryString;
        }
    }
}
