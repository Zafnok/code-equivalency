using System.IO;

namespace Equiv.Samples.RuntimeRowSyncPosition;

public static class Position
{
    public static long Rest(FileStream s)
    {
        var b = new byte[16];
        s.Read(b, 0, b.Length);
        return s.Length - s.Position;
    }
}
