using Equiv.Cli;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M5-001 criterion 6 (ADR 0033): the real <c>equiv mcp</c> process, driven over stdio by the SDK's own client,
/// returns for every directory under <c>samples/</c> the SARIF log <c>equiv compare</c> writes for it, once run timestamps
/// and absolute paths are normalised (<see cref="SarifNormalizer"/>, the same one the sample snapshots use).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class McpIntegrationTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Mcp_Compare_MatchesCliSarif(string sample)
    {
        string sampleDir = Path.Combine(SamplesRoot, sample);
        string legacy = Directory.GetFiles(Path.Combine(sampleDir, "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(sampleDir, "modern"), "*.slnx").Single();

        string cliJson = RunCli(legacy, modern);
        (string summary, string mcpJson) = await CompareOverMcp(legacy, modern).ConfigureAwait(true);

        Assert.Equal(SarifNormalizer.Normalize(cliJson, sampleDir), SarifNormalizer.Normalize(mcpJson, sampleDir));

        // The verdict counts, then the review list's lines as `equiv compare` prints them (ticket P2-064).
        Assert.Matches(
            @"^Equivalent \d+, Divergent \d+, Unknown \d+, skipped projects \d+, exit code \d+\nreview list: \d+ groups for \d+ flagged results(\n  EQ00[236] count=\d+ rank=\d+\.\d+ \S[^\n]*)*$",
            summary);
    }

    private static string RunCli(string legacy, string modern)
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M5-001-{Guid.NewGuid():N}.sarif");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            Program.Main(["compare", "--legacy", legacy, "--modern", modern, "--out", outPath]);
            return File.ReadAllText(outPath);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(outPath);
        }
    }

    /// <summary>Launches <c>equiv mcp</c> from this test's own output folder (the CLI's assembly is copied there) and calls <c>compare</c>.</summary>
    private static async Task<(string Summary, string Sarif)> CompareOverMcp(string legacy, string modern)
    {
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Name = "equiv",
            Command = "dotnet",
            Arguments = [Path.Combine(AppContext.BaseDirectory, "Equiv.Cli.dll"), "mcp"],
            StandardErrorLines = static _ => { },
        });
        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);
        try
        {
            Assert.Equal("equiv", client.ServerInfo.Name);
            CallToolResult result = await client.CallToolAsync(
                "compare",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["legacy"] = legacy, ["modern"] = modern },
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(true);

            Assert.NotEqual(true, result.IsError);
            Assert.Equal(2, result.Content.Count);
            return (Text(result.Content[0]), Text(result.Content[1]));
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static string Text(ContentBlock block) => Assert.IsType<TextContentBlock>(block).Text;
}
