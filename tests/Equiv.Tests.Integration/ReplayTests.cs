using Equiv.Cli;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M4-009 criterion 6: <c>equiv compare --execute</c> on <c>samples/removed-null-check</c>, end to end on both real
/// runtimes. The model's <c>name = null</c> makes the legacy method throw <c>ArgumentNullException</c> on .NET Framework 4.8
/// and the modern one <c>NullReferenceException</c> on .NET 10, so the Divergent is reproduced. Its SARIF, which differs from
/// <c>expected.sarif.json</c> only by <c>replay</c> and the rank a reproduced Divergent's review group gets (ticket P2-064),
/// is checked in as <c>removed-null-check.execute.sarif</c>. Windows only,
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

                // Only the pre-existing note, not ADR 0038's progress lines, which this test does not check.
                "--verbosity", "quiet",
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
        Assert.Equal(
            "note: --execute runs code from both solutions on this machine, in a temporary working directory; it is not sandboxed, "
            + "so absolute paths, the registry and the network are still reachable" + Environment.NewLine,
            error.ToString());
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

    /// <summary>
    /// Ticket P2-245: <c>samples/private-field-receiver</c>'s Divergent is a private method that reads two fields, of a class
    /// with no public parameterless constructor. Its replay binds the method by its exact signature on .NET Framework 4.8
    /// and on .NET 10, builds each receiver without a constructor from the model's fields, and reproduces, saying so in
    /// <c>replayInputs</c> (ADR 0062).
    /// </summary>
    [Fact]
    public void Replay_APrivateMethodOnAReceiverBuiltFromTheModel_Reproduces()
    {
        (int exitCode, SarifLog log) = Execute("private-field-receiver", "PrivateFieldReceiver");

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Result result = Assert.Single(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ002", StringComparison.Ordinal));
        Assert.Equal("reproduced", result.GetProperty<string>("replay"));
        Dictionary<string, List<Dictionary<string, string>>> inputs = result.GetProperty<Dictionary<string, List<Dictionary<string, string>>>>("replayInputs");
        Assert.All(
            [inputs["legacy"], inputs["modern"]],
            static side => Assert.Equal(("this", "uninitialized Account.balance, Account.owner"), (Assert.Single(side)["name"], side[0]["built"])));
    }

    /// <summary>
    /// Ticket P2-249: <c>samples/delegate-argument-answers</c>'s Divergent passes a delegate the model's runs call. The
    /// counterexample carries the model's answer to that call, a negative unit price, and the replay builds the delegate
    /// as that answer on .NET Framework 4.8 and on .NET 10: legacy throws, modern returns, and the Divergent reproduces,
    /// <c>replayInputs</c> saying the delegate was built as one (ADR 0062 decision 3).
    /// </summary>
    [Fact]
    public void Replay_ADelegateTheModelCalls_AnswersAsTheModelAndReproduces()
    {
        (int exitCode, SarifLog log) = Execute("delegate-argument-answers", "DelegateArgumentAnswers");

        Assert.Equal(ExitCodes.Divergent, exitCode);
        Result result = Assert.Single(log.Runs[0].Results, static r => string.Equals(r.RuleId, "EQ002", StringComparison.Ordinal));
        Assert.Equal("reproduced", result.GetProperty<string>("replay"));
        Dictionary<string, List<Dictionary<string, string>>> inputs = result.GetProperty<Dictionary<string, List<Dictionary<string, string>>>>("replayInputs");
        Assert.All(
            [inputs["legacy"], inputs["modern"]],
            static side => Assert.Equal([("price", "delegate"), ("count", "value")], side.Select(static i => (i["name"], i["built"]))));
    }

    /// <summary>
    /// <c>equiv compare --execute</c> on the sample <paramref name="sample"/>, whose solutions are named after
    /// <paramref name="name"/>: its exit code and its SARIF.
    /// </summary>
    private static (int ExitCode, SarifLog Log) Execute(string sample, string name)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", sample));
        string outPath = Path.Combine(Path.GetTempPath(), $"equiv-{sample}-{Guid.NewGuid():N}.sarif");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            int exitCode = Program.Main(
            [
                "compare",
                "--legacy", Path.Combine(root, "legacy", $"Equiv.Samples.{name}.Legacy.sln"),
                "--modern", Path.Combine(root, "modern", $"Equiv.Samples.{name}.Modern.slnx"),
                "--out", outPath,
                "--execute",
                "--verbosity", "quiet",
            ]);
            return (exitCode, SarifLog.Load(outPath));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            File.Delete(outPath);
        }
    }
}
