using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M5-002 criterion 5: <c>equiv mcp --execute</c>'s real <c>probe</c> tool, driven over stdio, calls
/// <c>samples/removed-null-check</c>'s <c>Greet</c> with a <c>null</c> argument and gets back the same two outcomes
/// <see cref="ReplayTests"/> observes through <c>--execute</c>'s replay: the legacy side throws
/// <c>ArgumentNullException</c> on .NET Framework 4.8 and the modern side throws <c>NullReferenceException</c> on .NET 10.
/// Windows only, like the rest of this project. Ticket P2-242 adds <c>samples/float-arithmetic</c>'s struct arguments, struct
/// result and constructor, on the same two runtimes; ticket P2-240, <c>samples/package-runtime-asset</c>'s package runtime asset;
/// ticket P2-254, <c>samples/relative-full-path</c>'s path that holds the working directory.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class ProbeTests
{
    private const string Identity = "Equiv.Samples.RemovedNullCheck.Greeter::Greet(string)";

    [Fact]
    public async Task Probe_RemovedNullCheck_NullDiffers()
    {
        using JsonDocument document = await ProbeAsync("removed-null-check", "RemovedNullCheck", Identity, [null]).ConfigureAwait(true);

        Assert.False(document.RootElement.GetProperty("equal").GetBoolean());
        Assert.Equal("Threw", document.RootElement.GetProperty("legacy").GetProperty("kind").GetString());
        Assert.Contains("ArgumentNullException", document.RootElement.GetProperty("legacy").GetProperty("canonical").GetString(), StringComparison.Ordinal);
        Assert.Equal("Threw", document.RootElement.GetProperty("modern").GetProperty("kind").GetString());
        Assert.Contains("NullReferenceException", document.RootElement.GetProperty("modern").GetProperty("canonical").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P2-242: <c>samples/float-arithmetic</c>'s <c>Warmer</c> takes two <c>Celsius</c> structs and returns one, and
    /// its two sides break a tie differently, so <c>+0</c> against <c>-0</c> returns a different struct on each runtime,
    /// compared field by field.
    /// </summary>
    [Fact]
    public async Task Probe_FloatArithmetic_AStructResultDiffersOnASignedZero()
    {
        object?[] arguments = [Degrees(0.0), Degrees(-0.0)];

        using JsonDocument document = await ProbeAsync("float-arithmetic", "FloatArithmetic", Warmer, arguments).ConfigureAwait(true);

        Assert.Equal(("Returned", "[\"0x0000000000000000\"]"), Outcome(document, "legacy"));
        Assert.Equal(("Returned", "[\"0x8000000000000000\"]"), Outcome(document, "modern"));
        Assert.False(document.RootElement.GetProperty("equal").GetBoolean());
    }

    /// <summary>Ticket P2-242: a constructor is called like a static method, and its struct is the same on both runtimes.</summary>
    [Fact]
    public async Task Probe_FloatArithmetic_AConstructorReturnsItsStruct()
    {
        using JsonDocument document = await ProbeAsync("float-arithmetic", "FloatArithmetic", "Equiv.Samples.FloatArithmetic.Celsius::.ctor(double)", [-0.0]).ConfigureAwait(true);

        Assert.Equal("[\"0x8000000000000000\"]", document.RootElement.GetProperty("legacy").GetProperty("canonical").GetString());
        Assert.Equal("[\"0x8000000000000000\"]", document.RootElement.GetProperty("modern").GetProperty("canonical").GetString());
        Assert.True(document.RootElement.GetProperty("equal").GetBoolean());
    }

    /// <summary>
    /// Ticket P2-240: <c>samples/package-runtime-asset</c>'s modern project compiles against <c>System.Management</c>
    /// 4.7.0's reference assembly. Its driver runs the package's Windows runtime asset, as the project's build output
    /// would, so both runtimes build the same query, where the modern side used to throw <c>FileNotFoundException</c>.
    /// </summary>
    [Fact]
    public async Task Probe_PackageRuntimeAsset_RunsThePackagesImplementation()
    {
        using JsonDocument document = await ProbeAsync(
            "package-runtime-asset", "PackageRuntimeAsset", "Equiv.Samples.PackageRuntimeAsset.Wmi::Query(string)", ["Win32_Battery"]).ConfigureAwait(true);

        Assert.Equal(("Returned", "\"select * from Win32_Battery\""), Outcome(document, "legacy"));
        Assert.Equal(("Returned", "\"select * from Win32_Battery\""), Outcome(document, "modern"));
        Assert.True(document.RootElement.GetProperty("equal").GetBoolean());
    }

    /// <summary>
    /// Ticket P2-254: <c>samples/relative-full-path</c>'s <c>Full</c> returns <c>Path.GetFullPath</c> of a relative path, which
    /// holds the working directory. Both drivers start in the same one, under the probe's own temporary folder, so both
    /// runtimes return the same path, where each side used to return its own <c>cwd-&lt;random&gt;</c> folder.
    /// </summary>
    [Fact]
    public async Task Probe_RelativeFullPath_BothSidesSeeOneWorkingDirectory()
    {
        using JsonDocument document = await ProbeAsync(
            "relative-full-path", "RelativeFullPath", "Equiv.Samples.RelativeFullPath.Paths::Full(string)", ["s1\\s2\\"]).ConfigureAwait(true);

        (string? kind, string? canonical) = Outcome(document, "legacy");
        Assert.Equal("Returned", kind);
        Assert.Contains("\\\\cwd-", canonical, StringComparison.Ordinal);
        Assert.EndsWith("\\\\s1\\\\s2\\\\\"", canonical, StringComparison.Ordinal);
        Assert.Equal(("Returned", canonical), Outcome(document, "modern"));
        Assert.True(document.RootElement.GetProperty("equal").GetBoolean());
    }

    private const string Warmer =
        "Equiv.Samples.FloatArithmetic.Arithmetic::Warmer(Equiv.Samples.FloatArithmetic.Celsius,Equiv.Samples.FloatArithmetic.Celsius)";

    private static Dictionary<string, object?> Degrees(double degrees) => new(StringComparer.Ordinal) { ["Degrees"] = degrees };

    /// <summary>Calls <c>probe</c> on <paramref name="sample"/> over a real <c>equiv mcp --execute</c> and returns its JSON result.</summary>
    private static async Task<JsonDocument> ProbeAsync(string sample, string name, string identity, object?[] arguments)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", sample));
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = "equiv",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "Equiv.Cli.dll"), "mcp", "--execute"],
            StandardErrorLines = static _ => { },
        });
        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        try
        {
            CallToolResult result = await client.CallToolAsync(
                "probe",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["legacy"] = Path.Combine(root, "legacy", $"Equiv.Samples.{name}.Legacy.sln"),
                    ["modern"] = Path.Combine(root, "modern", $"Equiv.Samples.{name}.Modern.slnx"),
                    ["identity"] = identity,
                    ["arguments"] = arguments,
                },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            string text = Text(Assert.Single(result.Content));
            Assert.True(result.IsError != true, text);
            return JsonDocument.Parse(text);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static (string? Kind, string? Canonical) Outcome(JsonDocument document, string side) =>
        (document.RootElement.GetProperty(side).GetProperty("kind").GetString(), document.RootElement.GetProperty(side).GetProperty("canonical").GetString());

    private static string Text(ContentBlock block) => Assert.IsType<TextContentBlock>(block).Text;
}
