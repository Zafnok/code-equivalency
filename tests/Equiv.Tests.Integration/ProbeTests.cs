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
/// Windows only, like the rest of this project.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class ProbeTests
{
    private const string Identity = "Equiv.Samples.RemovedNullCheck.Greeter::Greet(string)";

    private static string Sample =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "removed-null-check"));

    [Fact]
    public async Task Probe_RemovedNullCheck_NullDiffers()
    {
        string legacy = Path.Combine(Sample, "legacy", "Equiv.Samples.RemovedNullCheck.Legacy.sln");
        string modern = Path.Combine(Sample, "modern", "Equiv.Samples.RemovedNullCheck.Modern.slnx");
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
                    ["legacy"] = legacy,
                    ["modern"] = modern,
                    ["identity"] = Identity,
                    ["arguments"] = new object?[] { null },
                },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.NotEqual(true, result.IsError);
            using JsonDocument document = JsonDocument.Parse(Text(Assert.Single(result.Content)));
            Assert.False(document.RootElement.GetProperty("equal").GetBoolean());
            Assert.Equal("Threw", document.RootElement.GetProperty("legacy").GetProperty("kind").GetString());
            Assert.Contains("ArgumentNullException", document.RootElement.GetProperty("legacy").GetProperty("canonical").GetString(), StringComparison.Ordinal);
            Assert.Equal("Threw", document.RootElement.GetProperty("modern").GetProperty("kind").GetString());
            Assert.Contains("NullReferenceException", document.RootElement.GetProperty("modern").GetProperty("canonical").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static string Text(ContentBlock block) => Assert.IsType<TextContentBlock>(block).Text;
}
