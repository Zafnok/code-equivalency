using System.Runtime.InteropServices;

namespace Equiv.Samples.ExternImportRuntime
{
    public static class Changed
    {
        // The library differs between the sides.
        [DllImport("b.dll", ExactSpelling = true)]
        public static extern int F();
    }
}
