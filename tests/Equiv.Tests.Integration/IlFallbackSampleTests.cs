using Equiv.Cli;
using Equiv.Core.Progress;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-016 on real samples (ADR 0039): <c>--il-fallback</c> lowers both pairs of <c>samples/il-fallback</c> from IL,
/// and the snapshot records their verdicts (criterion 6); without the flag, <c>samples/business-layer</c>'s stdout and SARIF
/// are what they were before the ticket (criterion 1). Shares the "Console" collection with the other classes that run
/// <see cref="CompareCommand.Run"/>, which prints to the console this class redirects.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class IlFallbackSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public async Task SampleVerdictsSnapshot()
    {
        (string json, _) = Compare("il-fallback", ilFallback: true);

        SarifLog log = SarifLog.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)));
        Assert.Equal(2, log.Runs[0].Results.Count);
        Assert.All(log.Runs[0].Results, static r => Assert.Equal("il", r.GetProperty<string>("lowering")));
        await VerifyXunit.Verifier.Verify(SarifNormalizer.Normalize(json, Path.Combine(SamplesRoot, "il-fallback")));
    }

    /// <summary>
    /// <c>samples/business-layer/expected.sarif.json</c> is the run before this ticket, which it does not change; the stdout
    /// snapshot is that run's one line.
    /// </summary>
    [Fact]
    public async Task WithoutTheFlagOutputIsUnchanged()
    {
        (string json, string stdout) = Compare("business-layer", ilFallback: false);

        string expected = await File.ReadAllTextAsync(Path.Combine(SamplesRoot, "business-layer", "expected.sarif.json"), TestContext.Current.CancellationToken);
        Assert.Equal(expected, SarifNormalizer.Normalize(json, Path.Combine(SamplesRoot, "business-layer")));
        await VerifyXunit.Verifier.Verify(stdout);
    }

    private static (string Json, string StdOut) Compare(string sample, bool ilFallback)
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, sample, "modern"), "*.slnx").Single();
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-016-{Guid.NewGuid():N}.sarif");
        TextWriter original = Console.Out;
        using StringWriter stdout = new();
        Console.SetOut(stdout);
        try
        {
            CompareCommand.Run(
                new CompareOptions(legacy, modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false) { IlFallback = ilFallback },
                [new CSharpFrontend()],
                new Z3Backend(),
                new FileReportSink(outPath),
                NullRunLog.Instance);
            return (File.ReadAllText(outPath), stdout.ToString());
        }
        finally
        {
            Console.SetOut(original);
            File.Delete(outPath);
        }
    }
}
