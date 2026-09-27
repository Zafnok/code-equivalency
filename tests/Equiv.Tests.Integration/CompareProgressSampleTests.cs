using System.Text.RegularExpressions;

using Equiv.Cli;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M4-012 criteria 2 and 3 on <c>samples/business-layer</c> (ADR 0038): with <c>--verbosity normal</c> and
/// <c>--execute</c>, stderr gets a start line and an end line for each of <c>load</c>, <c>verify</c>, <c>execute</c> and
/// <c>write</c>, the <c>--log</c> file gets the same lines, and stdout and the SARIF file are byte for byte those of the
/// same run with <c>--verbosity quiet</c>. Windows only, like the rest of this project.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class CompareProgressSampleTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static string Sample =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "business-layer"));

    [Fact]
    public void Normal_Reports_Every_Phase_On_Stderr_And_Changes_Nothing_Else()
    {
        string logPath = Path.Combine(Path.GetTempPath(), $"equiv-M4-012-{Guid.NewGuid():N}.log");
        try
        {
            (int quietExit, string quietOut, string quietError, byte[] quietSarif) = Compare("quiet", logPath: null);
            (int normalExit, string normalOut, string normalError, byte[] normalSarif) = Compare("normal", logPath);

            Assert.Equal(quietExit, normalExit);
            Assert.Equal(quietOut, normalOut);
            Assert.Equal(quietSarif, normalSarif);
            Assert.DoesNotContain("equiv: ", quietError, StringComparison.Ordinal);
            string[] progress = [.. Lines(normalError).Where(static line => line.StartsWith("equiv: ", StringComparison.Ordinal))];
            Assert.Equal(Lines(quietError), Lines(normalError).Where(static line => !line.StartsWith("equiv: ", StringComparison.Ordinal)), StringComparer.Ordinal);
            foreach (string phase in (string[])["load", "verify", "execute", "write"])
            {
                Assert.Single(progress, line => Regex.IsMatch(line, $@"^equiv: \+\d\d:\d\d:\d\d {phase} 0/\d+ \(0%\) eta=\?", RegexOptions.None, RegexTimeout));
                Assert.Single(progress, line => Regex.IsMatch(line, $@"^equiv: \+\d\d:\d\d:\d\d {phase} done in \d\d:\d\d:\d\d\.\d{{3}}; eta@25%=\S+ eta@50%=\S+ eta@75%=\S+ dropped=0$", RegexOptions.None, RegexTimeout));
            }

            Assert.Equal(progress, File.ReadAllLines(logPath), StringComparer.Ordinal);
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    private static (int ExitCode, string Out, string Error, byte[] Sarif) Compare(string verbosity, string? logPath)
    {
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-M4-012-{Guid.NewGuid():N}.sarif");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            string[] log = logPath is null ? [] : ["--log", logPath];
            int exitCode = Program.Main(
            [
                "compare",
                "--legacy", Path.Combine(Sample, "legacy", "Equiv.Samples.BusinessLayer.Legacy.sln"),
                "--modern", Path.Combine(Sample, "modern", "Equiv.Samples.BusinessLayer.Modern.slnx"),
                "--out", outPath,
                "--execute",
                "--verbosity", verbosity,
                .. log,
            ]);
            return (exitCode, output.ToString(), error.ToString(), File.ReadAllBytes(outPath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(outPath);
        }
    }

    private static string[] Lines(string text) => text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
