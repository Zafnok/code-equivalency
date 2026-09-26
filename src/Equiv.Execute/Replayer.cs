using Equiv.Core;
using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// Replays a Divergent's model on both real runtimes (ADR 0035 decision 2; ticket M4-009): the legacy driver on .NET
/// Framework 4.8 and the modern driver on .NET 10, each once, under the invariant culture. Differing canonical outcomes
/// reproduce the divergence and equal ones do not. A side that gives no comparable outcome (a result with no canonical
/// form, no answer in time, or arguments its driver could not build) makes the replay not constructible. The verdict is
/// never changed here.
/// </summary>
public sealed class Replayer(IDriverHost host)
{
    public const string Culture = "invariant";

    private readonly DriverRunner runner = new(host, RuntimeDiff.CaseTimeout);

    public ReplayResult Replay(ReplayPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Drivers is not { } drivers)
        {
            return ReplayResult.NotConstructible(plan.Reason);
        }

        ExecutionOutcome legacy = Once(drivers.Legacy, plan.Legacy);
        ExecutionOutcome modern = Once(drivers.Modern, plan.Modern);
        return (Obstacle("legacy", legacy) ?? Obstacle("modern", modern)) is { } reason
            ? ReplayResult.NotConstructible(reason)
            : RuntimeComparison.Same(legacy, modern) ? ReplayResult.NotReproduced(legacy, modern) : ReplayResult.Reproduced;
    }

    private ExecutionOutcome Once(string driver, ExecutionInput input) =>
        runner.Side(driver, new ExecutionRequest(new CallIdentity(driver), [input], [Culture]))[0];

    /// <summary>Why <paramref name="outcome"/> cannot be compared, or null when it returned or threw.</summary>
    private static string? Obstacle(string side, ExecutionOutcome outcome) => outcome.Kind is OutcomeKind.Returned or OutcomeKind.Threw
        ? null
        : $"the {side} side gave {OutcomeLine.Name(outcome.Kind)} {outcome.Canonical}";
}
