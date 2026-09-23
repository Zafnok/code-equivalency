using System.Text.RegularExpressions;

using Equiv.Cli;
using Equiv.Core.Reporting;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-014 on a real sample: <c>--lower-only</c> writes the lowering census of <c>samples/business-layer</c>
/// (acceptance criterion 5), and <c>--dry-run</c> reports each side's analysed line count (criterion 10). The census
/// snapshot is the sample's ratchet (ADR 0027 decision 5): every precision ticket is expected to change it, and each
/// change is reviewed. Shares the "Console" collection with <see cref="ComparePipelineTests"/>, since both run
/// <see cref="CompareCommand.Run"/>, which prints to the console this class redirects.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed partial class LoweringCensusTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private static string Legacy => Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", "legacy"), "*.sln").Single();

    private static string Modern => Directory.GetFiles(Path.Combine(SamplesRoot, "business-layer", "modern"), "*.slnx").Single();

    [Fact]
    public async Task BusinessLayerCensusSnapshot()
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M3-014-{Guid.NewGuid():N}.sarif");
        try
        {
            int exitCode = ExitCodes.UsageError;
            CaptureStdOut(() => exitCode = CompareCommand.Run(
                new CompareOptions(Legacy, Modern, outPath, BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: false, LowerOnly: true),
                [new CSharpFrontend()], new Z3Backend(), new FileReportSink(outPath)));

            Assert.Equal(ExitCodes.Success, exitCode);
            Run run = SarifLog.Load(outPath).Runs[0];
            Assert.Empty(run.Results);
            Assert.True(run.TryGetSerializedPropertyValue("loweringCensus", out string? census));
            await VerifyJson(census);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void DryRunOnASamplePairReportsEachSidesLineCount()
    {
        int exitCode = ExitCodes.UsageError;
        string output = CaptureStdOut(() => exitCode = CompareCommand.Run(
            new CompareOptions(Legacy, Modern, "equiv.sarif", BaselinePath: null, ConfigPath: null, FailOn: null, DryRun: true),
            [new CSharpFrontend()], new Z3Backend(), new FileReportSink("unused.sarif")));

        Assert.Equal(ExitCodes.Success, exitCode);
        Match counts = CountsLine.Match(output);
        Assert.True(counts.Success, output);
        Assert.True(int.Parse(counts.Groups["legacy"].Value, System.Globalization.CultureInfo.InvariantCulture) > 0, output);
        Assert.True(int.Parse(counts.Groups["modern"].Value, System.Globalization.CultureInfo.InvariantCulture) > 0, output);
        Assert.False(File.Exists("unused.sarif"));
    }

    private static string CaptureStdOut(Action action)
    {
        TextWriter original = Console.Out;
        using StringWriter writer = new();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    [GeneratedRegex(@"^analysed lines of code: legacy=(?<legacy>\d+) modern=(?<modern>\d+)\r?$", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CountsLine { get; }
}
