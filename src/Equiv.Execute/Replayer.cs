using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Execute.Testing;

namespace Equiv.Execute;

/// <summary>
/// Replays a Divergent's model on both real runtimes (ADR 0035 decision 2; ticket M4-009): the legacy driver on .NET
/// Framework 4.8 and the modern driver on .NET 10, each once, under the invariant culture, and also under <c>tr-TR</c> when
/// either body calls a member of the runtime-changes table, as differential testing does (ticket P2-038). Differing
/// canonical outcomes under any culture reproduce the divergence. Equal ones do not, and make the replay not constructible
/// when the plan's <see cref="ReplayPlan.AlikeReason"/> says they are no evidence (ticket P2-037), or when both sides threw
/// where the model's runs do not both throw, so the driver's receiver or arguments are not the model's (ticket P2-038).
/// Otherwise equal outcomes of an EQ006 Divergent (a runtime-changed callee in the model's call trace) are not applicable,
/// since EQ006 claims the member differs, not that the model's input shows it. A side that gives no comparable outcome (a
/// result with no canonical form, no answer in time, or arguments or a culture its driver could not build) makes the
/// replay not constructible. The verdict is never changed here.
/// </summary>
public sealed class Replayer(IDriverHost host)
{
    /// <summary>The culture every replay runs under first, and the <c>probe</c> tool's default (ticket M5-002).</summary>
    public const string Culture = "invariant";

    private readonly DriverRunner runner = new(host, RuntimeDiff.CaseTimeout);

    public ReplayResult Replay(ReplayPlan plan, Counterexample model, IrProcedure old, IrProcedure @new)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);

        if (plan.Drivers is not { } drivers)
        {
            return ReplayResult.NotConstructible(plan.Reason);
        }

        IReadOnlyList<string> cultures = DifferentialTester.Cultures(old, @new);
        List<ExecutionOutcome> legacy = runner.Side(drivers.Legacy, new ExecutionRequest(new CallIdentity(drivers.Legacy), [plan.Legacy], cultures));
        List<ExecutionOutcome> modern = runner.Side(drivers.Modern, new ExecutionRequest(new CallIdentity(drivers.Modern), [plan.Modern], cultures));
        if ((Obstacle("legacy", legacy) ?? Obstacle("modern", modern)) is { } reason)
        {
            return ReplayResult.NotConstructible(reason);
        }

        bool agree = legacy.Zip(modern).All(static p => RuntimeComparison.Same(p.First, p.Second));
        return (legacy[0].Kind, model, plan.AlikeReason) switch
        {
            _ when !agree => ReplayResult.Reproduced,
            (_, _, { } alike) => ReplayResult.NotConstructible(alike),
            (OutcomeKind.Threw, not { Old.Outcome: IrThrew, New.Outcome: IrThrew }, _) =>
                ReplayResult.NotConstructible($"both sides threw {legacy[0].Canonical}, which the model's runs do not: the receiver or an argument is not the model's"),
            _ when RuntimeChanged(model) => ReplayResult.NotApplicable(legacy[0], modern[0]),
            _ => ReplayResult.NotReproduced(legacy[0], modern[0]),
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

    /// <summary>Whether the model's call trace holds a runtime-changed callee, which is what makes the Divergent EQ006.</summary>
    private static bool RuntimeChanged(Counterexample model) =>
        model.Old.Trace.Concat(model.New.Trace).Any(static r => r.Callee.RuntimeChanged);

    /// <summary>Why one of <paramref name="outcomes"/> cannot be compared, or null when each returned or threw.</summary>
    private static string? Obstacle(string side, List<ExecutionOutcome> outcomes) => outcomes
        .Where(static o => !RuntimeComparison.Comparable(o))
        .Select(o => $"the {side} side gave {OutcomeLine.Name(o.Kind)} {o.Canonical}")
        .FirstOrDefault();
}
