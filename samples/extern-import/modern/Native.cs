using System.Runtime.InteropServices;

namespace Equiv.Samples.ExternImport;

public class Native
{
    // The same import on both sides.
    [DllImport("kernel32.dll", EntryPoint = "GetTickCount")]
    public static extern uint Ticks();

    // The library differs between the sides.
    [DllImport("b.dll")]
    public static extern int F();

    // The same on both sides: it calls the import that changed.
    public int M()
    {
        return F();
    }
}
