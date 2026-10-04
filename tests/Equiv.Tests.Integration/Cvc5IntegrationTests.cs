using System.Text.Json;

using Equiv.Cli;
using Equiv.Core;
using Equiv.Verify.Cvc5;

using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-033 criterion 5 (ADR 0050): the real cvc5, run as a process, answers a rung 1 query Z3 gives up on at the
/// default budget. cvc5 is never in the repository (ADR 0017), so these run only where <see cref="ExecutableVariable"/>
/// names the executable <c>tools/cvc5/fetch.ps1</c> fetches, which CI's Windows leg sets, and are skipped elsewhere.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class Cvc5IntegrationTests
{
    /// <summary>The environment variable naming the cvc5 executable to test against.</summary>
    public const string ExecutableVariable = "EQUIV_CVC5";

    private static string Sample =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "hard-for-z3"));

    /// <summary>
    /// <c>samples/hard-for-z3</c>: without cvc5 the pair is Unknown(timeout), which the sample's snapshot pins
    /// (<see cref="SamplesEndToEndTests"/>). With it, cvc5 finds the factors, Z3 completes the model from them, and the
    /// replay makes the pair Divergent, tagged with the solver.
    /// </summary>
    [Fact]
    public void Cvc5AnswersTheQueryZ3GivesUpOn_AndItsModelReplaysToADivergence()
    {
        string cvc5 = Executable();
        string configPath = Path.Combine(Path.GetTempPath(), $"equiv-P1-033-{Guid.NewGuid():N}.json");
        string outPath = Path.ChangeExtension(configPath, ".sarif");
        try
        {
            File.WriteAllText(configPath, $$"""{ "solvers": { "cvc5": { "path": {{JsonSerializer.Serialize(cvc5)}} } } }""");
            int exitCode = Silently(() => Program.Main(
            [
                "compare",
                "--legacy", Directory.GetFiles(Path.Combine(Sample, "legacy"), "*.sln").Single(),
                "--modern", Directory.GetFiles(Path.Combine(Sample, "modern"), "*.slnx").Single(),
                "--config", configPath,
                "--out", outPath,
            ]));

            Result result = Assert.Single(SarifLog.Load(outPath).Runs[0].Results);
            Assert.Equal(ExitCodes.Divergent, exitCode);
            Assert.Equal("EQ002", result.RuleId);
            Assert.Equal("bounded+cvc5", result.GetProperty<string>("proofMethod"));
            Dictionary<string, string> step = Assert.Single(result.GetProperty<List<Dictionary<string, string>>>("ladderTrace"));
            Assert.Equal(("bounded", "refuted", "cvc5 1.4.1"), (step["rung"], step["outcome"], step["solver"]));
            Assert.Contains("old(returned bool false", result.GetProperty<string>("model"), StringComparison.Ordinal);
            Assert.Contains("new(returned bool true", result.GetProperty<string>("model"), StringComparison.Ordinal);
            Assert.Contains("inputs(bv32 ", result.GetProperty<string>("model"), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(configPath);
            File.Delete(outPath);
        }
    }

    /// <summary>The process itself: its version, an unsatisfiable script, a satisfiable one with its values by name, one it cannot read, and its limits.</summary>
    [Fact]
    public void TheExecutableAnswersScripts()
    {
        Cvc5Solver solver = new(Executable());
        const string Small = """
            (set-option :produce-models true)
            (set-logic QF_BV)
            (declare-fun |in.left| () (_ BitVec 16))
            (declare-fun right () (_ BitVec 16))
            (declare-fun found () Bool)
            (assert (bvult #x0001 |in.left|))
            (assert (bvult |in.left| #x0100))
            (assert (bvult #x0001 right))
            (assert (bvult right #x0100))
            (assert (= found (= (bvmul |in.left| right) #xec4b)))
            (assert found)
            (check-sat)
            (get-value (|in.left| right found))

            """;

        // Factoring the sample's semiprime through a 64-bit multiplier with no help from the encoding: minutes of work.
        const string Hard = """
            (set-logic QF_BV)
            (declare-fun left () (_ BitVec 64))
            (declare-fun right () (_ BitVec 64))
            (assert (bvult #x0000000000000001 left))
            (assert (bvult left #x0000000100000000))
            (assert (bvult #x0000000000000001 right))
            (assert (bvult right #x0000000100000000))
            (assert (= (bvmul left right) #x000fffff970000ab))
            (check-sat)

            """;

        Assert.Equal("1.4.1", solver.Version);
        Assert.IsType<SmtUnsat>(solver.Ask("(set-logic QF_BV)\n(declare-fun p () Bool)\n(assert (and p (not p)))\n(check-sat)\n", TimeSpan.FromSeconds(60)));
        SmtSat sat = Assert.IsType<SmtSat>(solver.Ask(Small, TimeSpan.FromSeconds(60)));
        Assert.Equal("true", sat.Values["found"]);
        Assert.Equal(0xec4bu, Convert.ToUInt32(sat.Values["|in.left|"][2..], 2) * Convert.ToUInt32(sat.Values["right"][2..], 2));
        Assert.StartsWith("(error ", Assert.IsType<SmtUnknown>(solver.Ask("(set-logic QF_BV)\n(check-sat", TimeSpan.FromSeconds(60))).Reason, StringComparison.Ordinal);

        // A resource limit of a thousand steps ends the search almost at once; a wall-clock limit of a millisecond ends the process.
        Assert.Equal("unknown", Assert.IsType<SmtUnknown>(new Cvc5Solver(Executable()) { ResourceLimit = 1_000 }.Ask(Hard, TimeSpan.FromSeconds(60))).Reason);
        Assert.Equal("cvc5 interrupted by timeout.", Assert.IsType<SmtUnknown>(new Cvc5Solver(Executable()) { ResourceLimit = 1_000_000_000_000 }.Ask(Hard, TimeSpan.FromMilliseconds(1))).Reason);
    }

    /// <summary>The configured executable; the test is skipped when none is, and fails when the one named is not there.</summary>
    private static string Executable()
    {
        string? cvc5 = Environment.GetEnvironmentVariable(ExecutableVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(cvc5), $"no cvc5 configured: run tools/cvc5/fetch.ps1 and set {ExecutableVariable} to the executable it prints");
        Assert.True(File.Exists(cvc5), $"{ExecutableVariable} names {cvc5}, which does not exist");
        return cvc5!;
    }

    private static int Silently(Func<int> action)
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter output = new();
        using StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
