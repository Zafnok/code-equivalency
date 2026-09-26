using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class NetStandardShimsTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public void TheNewestSdkWithTheFolderWinsUnderDotnetRoot()
    {
        string dotnet = Path.Combine(_fixture.Root, "dotnet");
        string newest = Shims(dotnet, "10.0.100");
        Shims(dotnet, "9.0.300");
        Shims(dotnet, "8.0.100-rc.1");
        Directory.CreateDirectory(Path.Combine(dotnet, "sdk", "11.0.100"));
        Directory.CreateDirectory(Path.Combine(dotnet, "sdk", "NuGetFallbackFolder"));

        Assert.Equal(newest, NetStandardShims.Find(name => name is "DOTNET_ROOT" ? dotnet : null));
    }

    [Fact]
    public void DotnetRootWinsOverThePath()
    {
        string rooted = Shims(Path.Combine(_fixture.Root, "rooted"), "10.0.100");
        string onPath = Path.Combine(_fixture.Root, "onpath");
        Shims(onPath, "10.0.100");
        _fixture.Write(Path.Combine("onpath", "dotnet"), string.Empty);
        _fixture.Write(Path.Combine("onpath", "dotnet.exe"), string.Empty);

        Assert.Equal(rooted, NetStandardShims.Find(name => name switch { "DOTNET_ROOT" => Path.Combine(_fixture.Root, "rooted"), "PATH" => onPath, _ => null }));
    }

    [Fact]
    public void WithoutDotnetRootTheDotnetOnThePathIsUsed()
    {
        string dotnet = Path.Combine(_fixture.Root, "dotnet");
        string shims = Shims(dotnet, "10.0.100");
        _fixture.Write(Path.Combine("dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), string.Empty);
        string path = string.Join(Path.PathSeparator, Path.Combine(_fixture.Root, "empty"), dotnet);

        Assert.Equal(shims, NetStandardShims.Find(name => name switch { "DOTNET_ROOT" => string.Empty, "PATH" => path, _ => null }));
    }

    [Fact]
    public void ASymbolicLinkOnThePathIsFollowedToTheInstallation()
    {
        string dotnet = Path.Combine(_fixture.Root, "dotnet");
        string shims = Shims(dotnet, "10.0.100");
        string target = _fixture.Write(Path.Combine("dotnet", "dotnet"), string.Empty);
        string bin = Path.Combine(_fixture.Root, "bin");
        Directory.CreateDirectory(bin);
        string hop = Path.Combine(_fixture.Root, "hop");
        Directory.CreateDirectory(hop);
        try
        {
            // Two links, as /usr/bin/dotnet -> /etc/alternatives/dotnet -> the installation: only the final target has the SDKs.
            File.CreateSymbolicLink(Path.Combine(hop, "dotnet"), target);
            File.CreateSymbolicLink(Path.Combine(bin, "dotnet"), Path.Combine(hop, "dotnet"));
        }
        catch (IOException)
        {
            // Creating symbolic links needs a privilege on Windows; the Linux leg covers this case.
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        Assert.Equal(shims, NetStandardShims.Find(name => name is "PATH" ? bin : null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoDotnetMeansNoShims(string? path) =>
        Assert.Null(NetStandardShims.Find(name => name is "PATH" ? path : null));

    [Fact]
    public void AnSdkWithoutTheFolderHasNoShims()
    {
        string dotnet = Path.Combine(_fixture.Root, "dotnet");
        Directory.CreateDirectory(Path.Combine(dotnet, "sdk", "10.0.100"));

        Assert.Null(NetStandardShims.Find(name => name is "DOTNET_ROOT" ? dotnet : null));
    }

    public void Dispose() => _fixture.Dispose();

    private static string Shims(string dotnet, string sdk)
    {
        string shims = Path.Combine(dotnet, "sdk", sdk, "Microsoft", "Microsoft.NET.Build.Extensions", "net461", "lib");
        Directory.CreateDirectory(shims);
        return shims;
    }
}
