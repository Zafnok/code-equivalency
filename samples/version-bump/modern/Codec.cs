using System.IO;

namespace Equiv.Samples.VersionBump;

public class Codec
{
    public string Name(BinaryReader reader)
    {
        return reader.ReadString();
    }

    public string Format(double value)
    {
        return value.ToString();
    }
}
