using System.Runtime.InteropServices;

namespace Equiv.Samples.SameRuntimeCleanup;

public static partial class Native
{
    public static partial bool Beep(uint frequency, uint duration);

    // The shape the interop generator gives a [LibraryImport] method, written by hand: the code is in
    // the implementing part, and what it calls is in the attribute of a local extern function.
    public static partial bool Beep(uint frequency, uint duration)
    {
        return Tone(frequency, duration) != 0;

        [DllImport("kernel32.dll", EntryPoint = "Beep", SetLastError = true)]
        static extern int Tone(uint frequency, uint duration);
    }
}
