using System;
using System.Runtime.InteropServices;

namespace Equiv.Samples.ExternImportRuntime
{
    public static class Native
    {
        // Spelled exactly, with no COM object: no interop change from .NET Core 3.0 to .NET 7 reaches it.
        [DllImport("kernel32.dll", EntryPoint = "GetTickCount", ExactSpelling = true)]
        public static extern uint Ticks();

        // Not spelled exactly: .NET 5 stopped probing for an A or W suffix off Windows.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBox(IntPtr window, string text, string caption, uint type);
    }
}
