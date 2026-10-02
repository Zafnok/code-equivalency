using System.Security.Cryptography;
using System.Text;

using Equiv.Frontend.CSharp.Execution;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-052: the key a run's replay drivers are signed with. Its two blobs are read back here field by field, as the
/// compiler reads them, and the pair signs what the public key verifies.
/// </summary>
public sealed class DriverKeyTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("driver-key-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void AKeyIsGeneratedOncePerInstance()
    {
        DriverKey key = new();

        Assert.Equal(key.PublicKey, key.PublicKey);
        Assert.NotEqual(key.PublicKey, new DriverKey().PublicKey);
    }

    [Fact]
    public void Write_PutsThePairInTheFolder()
    {
        string path = new DriverKey().Write(directory);

        Assert.Equal(Path.Combine(directory, "EquivReplay.snk"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ThePairSignsWhatThePublicKeyVerifies()
    {
        DriverKey key = new();
        byte[] pair = File.ReadAllBytes(key.Write(directory));
        byte[] publicKey = [.. key.PublicKey];
        byte[] data = Encoding.ASCII.GetBytes("equiv");

        using BinaryReader header = new(new MemoryStream(publicKey));
        Assert.Equal(0x2400u, header.ReadUInt32());
        Assert.Equal(0x8004u, header.ReadUInt32());
        Assert.Equal((uint)(publicKey.Length - 12), header.ReadUInt32());
        using RSA verifier = RSA.Create(Read(publicKey.AsSpan(12).ToArray(), 0x06, "RSA1", isPrivate: false));
        using RSA signer = RSA.Create(Read(pair, 0x07, "RSA2", isPrivate: true));

        Assert.Equal(2048, signer.KeySize);
        Assert.True(verifier.VerifyData(data, signer.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    /// <summary>A CryptoAPI RSA key blob's fields, in the order <c>PUBLICKEYBLOB</c> and <c>PRIVATEKEYBLOB</c> give them.</summary>
    private static RSAParameters Read(byte[] blob, byte type, string magic, bool isPrivate)
    {
        using BinaryReader reader = new(new MemoryStream(blob));
        Assert.Equal(type, reader.ReadByte());
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(0x2400u, reader.ReadUInt32());
        Assert.Equal(magic, Encoding.ASCII.GetString(reader.ReadBytes(4)));
        int bytes = (int)reader.ReadUInt32() / 8;
        Assert.Equal(65537u, reader.ReadUInt32());
        RSAParameters parameters = new() { Exponent = [1, 0, 1], Modulus = BigEndian(reader, bytes) };
        if (isPrivate)
        {
            parameters.P = BigEndian(reader, bytes / 2);
            parameters.Q = BigEndian(reader, bytes / 2);
            parameters.DP = BigEndian(reader, bytes / 2);
            parameters.DQ = BigEndian(reader, bytes / 2);
            parameters.InverseQ = BigEndian(reader, bytes / 2);
            parameters.D = BigEndian(reader, bytes);
        }

        Assert.Equal(blob.Length, reader.BaseStream.Position);
        return parameters;
    }

    private static byte[] BigEndian(BinaryReader reader, int length)
    {
        byte[] bytes = reader.ReadBytes(length);
        Array.Reverse(bytes);
        return bytes;
    }
}
