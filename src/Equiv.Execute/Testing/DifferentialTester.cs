using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Execute.Inputs;

namespace Equiv.Execute.Testing;

/// <summary>
/// Runs an Unknown pair on generated inputs on both real runtimes (ADR 0035 decision 3; ticket P1-008): the plan's seeds
/// first, then the M3-032 generators' stream from <see cref="Seed"/>, as many as the input budget, each input under the invariant culture, and also
/// under <c>tr-TR</c> when either body calls a member of the runtime-changes table. One driver process per side streams
/// every case. Each input's species is both outcome classes per culture, whether the outcomes are all equal, and both
/// sides' IR path signatures (<see cref="DifferentialTesting.SpeciesDefinition"/>); since "equal" is part of it, a
/// divergent input is a new species for as long as none has been seen. Testing stops at the first divergence that a
/// rerun of the same case in fresh processes repeats, at <see cref="TestingOptions.Target"/> once
/// <see cref="MinimumInputs"/> inputs have run, or at the budget. A side that cannot build a case's arguments stops it as
/// not constructible. Nothing here can yield Equivalent.
/// </summary>
public sealed class DifferentialTester(IDriverHost host, TestingOptions options, TimeProvider time)
{
    /// <summary>The fewest inputs after which the target can stop testing.</summary>
    public const int MinimumInputs = 1_000;

    /// <summary>The generators' seed, fixed so a run's inputs, and its report, are the same every time.</summary>
    public const ulong Seed = 0;

    private const string Invariant = "invariant";

    private const string Turkish = "tr-TR";

    public TestingOutcome Test(TestingPlan plan, IrProcedure old, IrProcedure @new)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);

        if (plan.Drivers is not { } drivers)
        {
            return new TestingOutcome(DifferentialTesting.Unconstructible(plan.Reason), Observed: null);
        }

        IReadOnlyList<string> cultures = Cultures(old, @new);
        using DriverStream legacy = new(host, drivers.Legacy, RuntimeDiff.CaseTimeout);
        using DriverStream modern = new(host, drivers.Modern, RuntimeDiff.CaseTimeout);
        SpeciesTally tally = new();
        long start = time.GetTimestamp();

        // The input budget is the number of inputs taken; the time budget breaks out early.
        foreach (ExecutionInput input in plan.Seeds.Concat(InputGenerator.Stream(plan.Parameters, Seed, options.Inputs)).Take(options.Inputs))
        {
            List<(ExecutionOutcome Legacy, ExecutionOutcome Modern)> runs = [.. cultures.Select(c => (legacy.Run(input, c), modern.Run(input, c)))];
            if (Obstacle(runs) is { } reason)
            {
                return new TestingOutcome(DifferentialTesting.Unconstructible(reason), Observed: null);
            }

            int divergent = runs.FindIndex(static r => Diverges(r.Legacy, r.Modern));
            if (divergent >= 0 && Repeats(drivers, runs[divergent].Legacy, runs[divergent].Modern))
            {
                return new TestingOutcome(Testing: null, new ObservedDivergence(runs[divergent].Legacy, runs[divergent].Modern));
            }

            tally.Add(Species(runs, old, @new, input));
            if (tally.Inputs >= MinimumInputs && tally.DiscoveryProbability < options.Target)
            {
                return Tested(tally, TestingStop.Target);
            }

            if (time.GetElapsedTime(start) >= options.Time)
            {
                break;
            }
        }

        return Tested(tally, TestingStop.Budget);
    }

    private static TestingOutcome Tested(SpeciesTally tally, TestingStop stop) =>
        new(DifferentialTesting.Tested(tally.Inputs, tally.Species, tally.Singletons, tally.DiscoveryProbability, stop), Observed: null);

    /// <summary>
    /// The invariant culture, and also <c>tr-TR</c> when either body calls a member of the runtime-changes table; replay
    /// runs a Divergent's model under the same set (ticket P2-038).
    /// </summary>
    internal static IReadOnlyList<string> Cultures(IrProcedure old, IrProcedure @new) =>
        RuntimeSensitive(old) || RuntimeSensitive(@new) ? [Invariant, Turkish] : [Invariant];

    /// <summary>Whether <paramref name="body"/> calls a member whose behaviour the runtime-changes table says differs.</summary>
    private static bool RuntimeSensitive(IrProcedure body) =>
        body.Blocks.SelectMany(static b => b.Instructions).Any(static i => i is IrCall { Callee.RuntimeChanged: true } or IrPure { RuntimeSensitive: true });

    /// <summary>Why a side could not run a case at all: its driver could not build the arguments or the culture.</summary>
    private static string? Obstacle(List<(ExecutionOutcome Legacy, ExecutionOutcome Modern)> runs) =>
        runs.SelectMany(static r => new[] { ("legacy", r.Legacy), ("modern", r.Modern) })
            .Where(static s => s.Item2.Kind == OutcomeKind.NotConstructible)
            .Select(static s => $"the {s.Item1} side gave NotConstructible {s.Item2.Canonical}")
            .FirstOrDefault();

    private static bool Diverges(ExecutionOutcome legacy, ExecutionOutcome modern) =>
        RuntimeComparison.Comparable(legacy) && RuntimeComparison.Comparable(modern) && !RuntimeComparison.Same(legacy, modern);

    /// <summary>Whether rerunning the case in fresh processes gives both sides' outcomes again, so the divergence is not noise.</summary>
    private bool Repeats(ExecutionDrivers drivers, ExecutionOutcome legacy, ExecutionOutcome modern)
    {
        using DriverStream legacyAgain = new(host, drivers.Legacy, RuntimeDiff.CaseTimeout);
        using DriverStream modernAgain = new(host, drivers.Modern, RuntimeDiff.CaseTimeout);
        return RuntimeComparison.Same(legacyAgain.Run(legacy.Input, legacy.Culture), legacy)
            && RuntimeComparison.Same(modernAgain.Run(modern.Input, modern.Culture), modern);
    }

    /// <summary>One input's species, as text: each culture's two outcome classes, whether every pair is equal, and both IR paths.</summary>
    internal static string Species(List<(ExecutionOutcome Legacy, ExecutionOutcome Modern)> runs, IrProcedure old, IrProcedure @new, ExecutionInput input) =>
        string.Join(" | ", runs.Select(static r => $"{OutcomeClass.Of(r.Legacy)} / {OutcomeClass.Of(r.Modern)}"))
        + $" | equal {runs.TrueForAll(static r => RuntimeComparison.Same(r.Legacy, r.Modern))}"
        + $" | path {IrPathSignature.Of(old, input)} / {IrPathSignature.Of(@new, input)}";
}
