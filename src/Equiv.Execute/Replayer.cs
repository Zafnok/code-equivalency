using Equiv.Core;
using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// Replays a Divergent's model on both real runtimes (ADR 0035 decision 2; ticket M4-009): the legacy driver on .NET
/// Framework 4.8 and the modern driver on .NET 10, each once, under the invariant culture. Differing canonical outcomes
/// reproduce the divergence and equal ones do not. A side that gives no comparable outcome (a result with no canonical
/// form, no answer in time, or arguments its driver could not build) makes the replay not constructible, and so do equal
/// outcomes of a plan whose <see cref="ReplayPlan.AlikeReason"/> says they are no evidence (ticket P2-037). The verdict is
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

        ExecutionOutcome legacy = Once(drivers.Legacy, plan.Legacy, Culture);
        ExecutionOutcome modern = Once(drivers.Modern, plan.Modern, Culture);
        return (Obstacle("legacy", legacy) ?? Obstacle("modern", modern), RuntimeComparison.Same(legacy, modern), plan.AlikeReason) switch
        {
            ({ } reason, _, _) => ReplayResult.NotConstructible(reason),
            (_, false, _) => ReplayResult.Reproduced,
            (_, true, { } alike) => ReplayResult.NotConstructible(alike),
            _ => ReplayResult.NotReproduced(legacy, modern),
        };
    }

    /// <summary>
    /// Runs <paramref name="plan"/>'s case on each side once under <paramref name="culture"/>, with no comparison: an
    /// agent's own hunch about a pair (ADR 0035, ADR 0036; ticket M5-002's <c>probe</c>), never a verdict.
    /// </summary>
    public (ExecutionOutcome Legacy, ExecutionOutcome Modern) Run(ReplayPlan plan, string culture)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(culture);

        ExecutionDrivers drivers = plan.Drivers ?? throw new InvalidOperationException($"the plan is not constructible: {plan.Reason}");
        return (Once(drivers.Legacy, plan.Legacy, culture), Once(drivers.Modern, plan.Modern, culture));
    }

    private ExecutionOutcome Once(string driver, ExecutionInput input, string culture) =>
        runner.Side(driver, new ExecutionRequest(new CallIdentity(driver), [input], [culture]))[0];

    /// <summary>Why <paramref name="outcome"/> cannot be compared, or null when it returned or threw.</summary>
    private static string? Obstacle(string side, ExecutionOutcome outcome) => outcome.Kind is OutcomeKind.Returned or OutcomeKind.Threw
        ? null
        : $"the {side} side gave {OutcomeLine.Name(outcome.Kind)} {outcome.Canonical}";
}
