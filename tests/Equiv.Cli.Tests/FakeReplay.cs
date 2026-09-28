using System.Text.Json;

using Equiv.Core.Execution;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// A replay factory that plans the same two drivers for every pair and records what it was asked (ticket M4-009), and a
/// driver host that answers every case with the canned line of its driver. <see cref="Plan"/> plans a method without
/// parameters for every Unknown pair (ticket P1-008). <see cref="Probe"/> plans the same two drivers for an agent's own
/// arguments, or the canned <see cref="ProbeReason"/> when one is set (ticket M5-002).
/// </summary>
internal sealed class FakeReplay(string legacyAnswer, string modernAnswer) : IReplayDriverFactory, IDriverHost
{
    public List<(ProcedurePair Pair, Counterexample Counterexample, string Directory)> Creates { get; } = [];

    public List<(ProcedurePair Pair, Counterexample? Candidate, string Directory)> Plans { get; } = [];

    public List<(ProcedurePair Pair, IReadOnlyList<JsonElement> Arguments, string Directory)> Probes { get; } = [];

    public List<string> Starts { get; } = [];

    /// <summary>When set, <see cref="Probe"/> returns a not-constructible plan with this reason instead of a runnable one.</summary>
    public string? ProbeReason { get; set; }

    public ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory)
    {
        Creates.Add((pair, counterexample, directory));
        Assert.True(Directory.Exists(directory));
        return ReplayPlan.Runnable(new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["0"]), new ExecutionInput(["0"]));
    }

    public TestingPlan Plan(ProcedurePair pair, Counterexample? candidate, string directory)
    {
        Plans.Add((pair, candidate, directory));
        Assert.True(Directory.Exists(directory));
        return TestingPlan.Runnable(new ExecutionDrivers("legacy.exe", "modern.dll"), [], []);
    }

    public ReplayPlan Probe(ProcedurePair pair, IReadOnlyList<JsonElement> arguments, string directory)
    {
        Probes.Add((pair, arguments, directory));
        Assert.True(Directory.Exists(directory));
        return ProbeReason is { } reason
            ? ReplayPlan.NotConstructible(reason)
            : ReplayPlan.Runnable(new ExecutionDrivers("legacy.exe", "modern.dll"), new ExecutionInput(["0"]), new ExecutionInput(["0"]));
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
