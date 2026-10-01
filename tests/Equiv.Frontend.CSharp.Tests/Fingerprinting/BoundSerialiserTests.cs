using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.RuntimeChanges;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Tests.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Fingerprinting;

/// <summary>
/// Which bodies <see cref="BoundSerialiser"/> finds runtime-sensitive (ADR 0024), now decided inside the pair's runtime
/// interval (ADR 0040 decision 2; ticket P2-055): a rule the pair does not cross leaves the body congruent.
/// </summary>
public sealed class BoundSerialiserTests
{
    [Theory]
    [InlineData("net48", "net10.0", true)]
    [InlineData("net8.0", "net10.0", true)]
    [InlineData("net8.0", "net9.0", true)]
    [InlineData("net10.0", "net8.0", true)]
    [InlineData("net9.0", "net10.0", false)]
    [InlineData("net48", "net8.0", false)]
    [InlineData("net10.0", "net10.0", false)]
    public void FloatToIntIsSensitiveOnlyAcrossNet9(string legacy, string modern, bool expected)
    {
        Compilation compilation = Compile("int M(double d) => (int)d;");

        BodyFingerprint fingerprint = Fingerprint(compilation, Runtimes.Between(legacy, modern));

        Assert.Equal(expected, fingerprint.RuntimeSensitive);
        // The runtimes decide the flag, never the text: the same body hashes the same on every pair.
        Assert.Equal(Fingerprint(compilation, Runtimes.Migration).Sha256Hex, fingerprint.Sha256Hex);
    }

    /// <summary>
    /// A floating-point body is x87-sensitive on the side whose project alone runs on the 32-bit .NET Framework JIT,
    /// whatever the interval: two such sides agree, and a .NET (Core) project is never x87, even on x86.
    /// </summary>
    [Theory]
    [InlineData("net48", Platform.X86, "net10.0", Platform.AnyCpu, true, false)]
    [InlineData("net48", Platform.AnyCpu32BitPreferred, "net10.0", Platform.AnyCpu, true, false)]
    [InlineData("net48", Platform.X86, "net10.0", Platform.X86, true, false)]
    [InlineData("net48", Platform.AnyCpu, "net10.0", Platform.AnyCpu, false, false)]
    [InlineData("net48", Platform.X64, "net10.0", Platform.AnyCpu, false, false)]
    [InlineData("net48", Platform.X86, "net48", Platform.X86, false, false)]
    [InlineData("net48", Platform.AnyCpu32BitPreferred, "net472", Platform.X86, false, false)]
    [InlineData("net48", Platform.X86, "net48", Platform.AnyCpu, true, false)]
    [InlineData("net48", Platform.AnyCpu, "net48", Platform.X86, false, true)]
    [InlineData("net10.0", Platform.X86, "net48", Platform.X86, false, true)]
    [InlineData("net8.0", Platform.X86, "net10.0", Platform.X86, false, false)]
    public void X87OnlyWhenExactlyOneSideIsFramework32Bit(
        string legacyRuntime, Platform legacyPlatform, string modernRuntime, Platform modernPlatform, bool legacySensitive, bool modernSensitive)
    {
        Compilation legacy = Compile("double M(double d) => d * 2;", legacyPlatform);
        Compilation modern = Compile("double M(double d) => d * 2;", modernPlatform);

        (SideRuntime legacySide, SideRuntime modernSide) =
            SideRuntime.Of(SideRuntimeTests.On(legacyRuntime), legacy, SideRuntimeTests.On(modernRuntime), modern, RuntimeChangeTable.Load());

        Assert.Equal(legacySensitive, Fingerprint(legacy, legacySide).RuntimeSensitive);
        Assert.Equal(modernSensitive, Fingerprint(modern, modernSide).RuntimeSensitive);
    }

    /// <summary>
    /// A member's row applies only inside the interval: <c>String.IndexOf</c> changed in .NET 5, <c>Double.ToString</c> in
    /// .NET Core 3.0, <c>BinaryReader.ReadString</c> in .NET 9, and <c>Encoding.Default</c>'s row has no known change point,
    /// so it applies whenever the runtimes differ.
    /// </summary>
    [Theory]
    [InlineData("int M(string s) => s.IndexOf(\"x\");", "net48", "net10.0", true)]
    [InlineData("int M(string s) => s.IndexOf(\"x\");", "net8.0", "net10.0", false)]
    [InlineData("int M(string s) => s.IndexOf(\"x\");", "net10.0", "net10.0", false)]
    [InlineData("string M(double d) => d.ToString();", "net8.0", "net10.0", false)]
    [InlineData("string M(double d) => d.ToString();", "net48", "net8.0", true)]
    [InlineData("string M(System.IO.BinaryReader r) => r.ReadString();", "net8.0", "net10.0", true)]
    [InlineData("string M(System.IO.BinaryReader r) => r.ReadString();", "net48", "net8.0", false)]
    [InlineData("System.Text.Encoding M() => System.Text.Encoding.Default;", "net8.0", "net10.0", true)]
    [InlineData("System.Text.Encoding M() => System.Text.Encoding.Default;", "net8.0", "net8.0", false)]
    public void ARuntimeChangedMemberIsSensitiveOnlyInsideTheInterval(string member, string legacy, string modern, bool expected) =>
        Assert.Equal(expected, Fingerprint(Compile(member), Runtimes.Between(legacy, modern)).RuntimeSensitive);

    private static BodyFingerprint Fingerprint(Compilation compilation, SideRuntime runtime) =>
        BodyFingerprinter.Compute(
            compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single(), compilation, EquivConfig.Default, legacy: false, runtime)!;

    private static Compilation Compile(string member, Platform platform = Platform.AnyCpu)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"class C {{ {member} }}");
        return compilation.WithOptions(compilation.Options.WithPlatform(platform));
    }
}
