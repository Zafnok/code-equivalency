using Equiv.Samples.DependencyRebinding.Files;

namespace Equiv.Samples.DependencyRebinding
{
    public static class Probe
    {
        public static bool Has(IFs fs, string p) => fs.File.Exists(p);

        public static bool Clear(IFs fs, string p) => fs.File.Exists(p);
    }
}
