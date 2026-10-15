using System.Runtime.InteropServices;

namespace Equiv.Samples.ExternImportRuntime
{
    public static class Changed
    {
        // The library differs between the sides.
        [DllImport("a.dll", ExactSpelling = true)]
        public static extern int F();
    }
}
