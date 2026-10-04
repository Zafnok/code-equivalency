using Equiv.Core;

using Xunit;

namespace Equiv.Verify.Cvc5.Tests;

/// <summary>Ticket P1-033: cvc5 behind <see cref="ISmtSolver"/>, with the process stood in for.</summary>
public sealed class Cvc5SolverTests
{
    private const string Script = "(check-sat)";

    [Fact]
    public void Ask_RunsTheExecutableWithItsLimitsAndTheScript()
    {
        List<(string Path, string[] Arguments, string? Script, TimeSpan KillAfter)> runs = [];
        Cvc5Solver solver = new("tools/cvc5.exe", (path, arguments, script, killAfter) =>
        {
            runs.Add((path, [.. arguments], script, killAfter));
            return "unsat\n";
        });

        SmtAnswer answer = solver.Ask(Script, TimeSpan.FromMilliseconds(1_500));

        Assert.IsType<SmtUnsat>(answer);
        (string ranPath, string[] ranArguments, string? ranScript, TimeSpan killAfter) = Assert.Single(runs);
        Assert.Equal("tools/cvc5.exe", ranPath);
        Assert.Equal(["--arrays-exp", "--rlimit=2000000", "--tlimit=1500"], ranArguments);
        Assert.Equal(Script, ranScript);
        Assert.Equal(TimeSpan.FromMilliseconds(6_500), killAfter);
        Assert.Equal(TimeSpan.FromSeconds(5), Cvc5Solver.KillAfter);
        Assert.Equal(2_000_000, Cvc5Solver.DefaultResourceLimit);
        Assert.Equal(Cvc5Solver.DefaultResourceLimit, solver.ResourceLimit);
        Assert.Equal("cvc5", solver.Name);
    }

    [Fact]
    public void Ask_PassesTheResourceLimitItWasGiven()
    {
        string[] ran = [];
        Cvc5Solver solver = new("cvc5", (_, arguments, _, _) =>
        {
            ran = [.. arguments];
            return "unknown\n";
        })
        {
            ResourceLimit = 12_345_678_901,
        };

        SmtUnknown unknown = Assert.IsType<SmtUnknown>(solver.Ask(Script, TimeSpan.FromSeconds(60)));

        Assert.Equal("unknown", unknown.Reason);
        Assert.Equal(["--arrays-exp", "--rlimit=12345678901", "--tlimit=60000"], ran);
    }

    [Fact]
    public void Ask_AKilledProcessIsUnknown()
    {
        Cvc5Solver solver = new("cvc5", static (_, _, _, _) => null);

        SmtUnknown unknown = Assert.IsType<SmtUnknown>(solver.Ask(Script, TimeSpan.FromSeconds(1)));

        Assert.Equal("wall-clock limit: the process was killed", unknown.Reason);
    }

    [Fact]
    public void Version_IsReadOnceFromTheExecutable()
    {
        List<(string[] Arguments, string? Script, TimeSpan KillAfter)> runs = [];
        Cvc5Solver solver = new("cvc5", (_, arguments, script, killAfter) =>
        {
            runs.Add(([.. arguments], script, killAfter));
            return "cvc5 1.4.1 [git 2b2e844 on branch HEAD]\ncompiled as a unrestricted build with GCC version Clang 22.1.7\n";
        });

        Assert.Empty(runs);
        Assert.Equal("1.4.1", solver.Version);
        Assert.Equal("1.4.1", solver.Version);
        (string[] arguments, string? script, TimeSpan killAfter) = Assert.Single(runs);
        Assert.Equal(["--version"], arguments);
        Assert.Null(script);
        Assert.Equal(Cvc5Solver.KillAfter, killAfter);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cvc5: not a version")]
    public void Version_IsUnknownWhenTheExecutablePrintsNone(string? output)
    {
        Assert.Equal("unknown", new Cvc5Solver("cvc5", (_, _, _, _) => output).Version);
    }

    [Fact]
    public void Constructor_RejectsAnEmptyPath()
    {
        Assert.Throws<ArgumentException>(static () => new Cvc5Solver(" "));
        Assert.Throws<ArgumentNullException>(static () => new Cvc5Solver("cvc5").Ask(null!, TimeSpan.Zero));
    }
}
