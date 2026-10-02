using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// The strong-name key one run's replay drivers are signed with when their project is strong-named (ticket P2-052). A
/// strong-named assembly may name a friend only with its public key, so the <c>InternalsVisibleTo</c>
/// <see cref="ProjectEmitter"/> adds carries <see cref="PublicKey"/> and the driver is signed with the pair. The key is
/// generated on first use, so a run with no strong-named project generates none, and it is never reused across runs: it
/// grants nothing outside the folder the run emits into. Both forms are the ones <c>sn.exe</c> writes, which are what the
/// compiler reads: a <c>PRIVATEKEYBLOB</c> for the pair, and the 12-byte strong-name header over a <c>PUBLICKEYBLOB</c> for
/// the public key, every integer little-endian.
/// </summary>
internal sealed class DriverKey
{
    /// <summary>The key pair's file name in a project's replay folder.</summary>
    public const string FileName = ProjectEmitter.DriverAssembly + ".snk";

    private const int Bits = 2048;
    private const byte PublicBlob = 0x06;
    private const byte PrivateBlob = 0x07;
    private const uint RsaSign = 0x2400;
    private const uint Sha1 = 0x8004;

    private readonly Lazy<(ImmutableArray<byte> Public, byte[] Pair)> key = new(Generate);

    /// <summary>The public key as an assembly identity and an <c>InternalsVisibleTo</c> spell it.</summary>
    public ImmutableArray<byte> PublicKey => key.Value.Public;

    /// <summary>Writes the key pair into <paramref name="directory"/> as <see cref="FileName"/>; returns its path.</summary>
    public string Write(string directory)
    {
        string path = Path.Combine(directory, FileName);
        File.WriteAllBytes(path, key.Value.Pair);
        return path;
    }

    private static (ImmutableArray<byte> Public, byte[] Pair) Generate()
    {
        using RSA rsa = RSA.Create(Bits);
        RSAParameters p = rsa.ExportParameters(includePrivateParameters: true);
        byte[] publicBlob = Blob(PublicBlob, "RSA1", p, p.Modulus!);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(RsaSign);
        writer.Write(Sha1);
        writer.Write((uint)publicBlob.Length);
        writer.Write(publicBlob);
        return ([.. stream.ToArray()], Blob(PrivateBlob, "RSA2", p, p.Modulus!, p.P!, p.Q!, p.DP!, p.DQ!, p.InverseQ!, p.D!));
    }

    /// <summary>A CryptoAPI key blob: its header, the key's size and public exponent, then <paramref name="integers"/>.</summary>
    private static byte[] Blob(byte type, string magic, RSAParameters p, params byte[][] integers)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(type);
        writer.Write((byte)2);
        writer.Write((ushort)0);
        writer.Write(RsaSign);
        writer.Write(Encoding.ASCII.GetBytes(magic));
        writer.Write((uint)Bits);
        writer.Write(LittleEndian(p.Exponent!, sizeof(uint)));
        foreach (byte[] integer in integers)
        {
            writer.Write(LittleEndian(integer, integer.Length));
        }

        return stream.ToArray();
    }

    /// <summary>A big-endian integer as <paramref name="length"/> little-endian bytes.</summary>
    private static byte[] LittleEndian(byte[] bigEndian, int length)
    {
        byte[] bytes = new byte[length];
        bigEndian.AsSpan().CopyTo(bytes);
        bytes.AsSpan(0, bigEndian.Length).Reverse();
        return bytes;
    }
}
