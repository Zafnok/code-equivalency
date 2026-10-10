using System.IO;

namespace Equiv.Samples.RuntimeRowAsyncPosition;

public static class Position
{
    public static long Rest(FileStream s)
    {
        var b = new byte[16];
        s.ReadAsync(b, 0, b.Length).GetAwaiter().GetResult();
        return s.Length - s.Position;
    }
}
