using Equiv.Cli;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M4-009 criterion 6: <c>equiv compare --execute</c> on <c>samples/removed-null-check</c>, end to end on both real
/// runtimes. The model's <c>name = null</c> makes the legacy method throw <c>ArgumentNullException</c> on .NET Framework 4.8
/// and the modern one <c>NullReferenceException</c> on .NET 10, so the Divergent is reproduced. Its SARIF, which differs from
/// <c>expected.sarif.json</c> only by <c>replay</c>, is checked in as <c>removed-null-check.execute.sarif</c>. Windows only,
/// like the rest of this project.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class ReplayTests
{
    private static string Sample =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "removed-null-check"));

    private static string Snapshot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "removed-null-check.execute.sarif"));

    [Fact]
    public async Task Replay_RemovedNullCheck_Reproduces()
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M4-009-{Guid.NewGuid():N}.sarif");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        string json;
        SarifLog log;
        int exitCode;
        try
        {
            exitCode = Program.Main(
            [
                "compare",
                "--legacy", Path.Combine(Sample, "legacy", "Equiv.Samples.RemovedNullCheck.Legacy.sln"),
                "--modern", Path.Combine(Sample, "modern", "Equiv.Samples.RemovedNullCheck.Modern.slnx"),
                "--out", outPath,
                "--execute",
            ]);
            json = await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken);
            log = SarifLog.Load(outPath);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(outPath);
        }

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Assert.Equal("note: --execute runs code from both solutions on this machine" + Environment.NewLine, error.ToString());
        Result result = Assert.Single(log.Runs[0].Results);
        Assert.Equal("EQ002", result.RuleId);
        Assert.Equal("reproduced", result.GetProperty<string>("replay"));

        string normalized = SarifNormalizer.Normalize(json, Sample);
        if (!File.Exists(Snapshot))
        {
            await File.WriteAllTextAsync(Snapshot, normalized, TestContext.Current.CancellationToken);
            Assert.Fail("removed-null-check.execute.sarif was missing; wrote today's output. Review it, commit it, and re-run.");
        }

        Assert.Equal(await File.ReadAllTextAsync(Snapshot, TestContext.Current.CancellationToken), normalized);
    }
}
