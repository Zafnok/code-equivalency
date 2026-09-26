using Equiv.Core.Execution;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// A replay factory that plans the same two drivers for every pair and records what it was asked (ticket M4-009), and a
/// driver host that answers every case with the canned line of its driver.
/// </summary>
internal sealed class FakeReplay(string legacyAnswer, string modernAnswer) : IReplayDriverFactory, IDriverHost
{
    public List<(ProcedurePair Pair, Counterexample Counterexample, string Directory)> Creates { get; } = [];

    public List<string> Starts { get; } = [];

    public ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory)
    {
        Creates.Add((pair, counterexample, directory));
        Assert.True(Directory.Exists(directory));
        return ReplayPlan.Runnable(new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["0"]), new ExecutionInput(["0"]));
    }

    public IDriverSession Start(string driver)
    {
        Starts.Add(driver);
        return new Session(driver.EndsWith(".exe", StringComparison.Ordinal) ? legacyAnswer : modernAnswer);
    }

    private sealed class Session(string answer) : IDriverSession
    {
        public string? Exchange(string line, TimeSpan timeout) => answer;

        public void Dispose()
        {
        }
    }
}
