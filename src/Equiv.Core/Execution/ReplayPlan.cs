namespace Equiv.Core.Execution;

/// <summary>
/// How to replay one Divergent's model (ADR 0035 decision 2; ticket M4-009): the two drivers, and the one case each side's
/// driver runs, in <see cref="ExecutionInput"/>'s wire form. When the model cannot become a call on both sides,
/// <see cref="Drivers"/> is null and <see cref="Reason"/> says why.
/// </summary>
public sealed record ReplayPlan(ExecutionDrivers? Drivers, ExecutionInput Legacy, ExecutionInput Modern, string Reason)
{
    public static ReplayPlan Runnable(ExecutionDrivers drivers, ExecutionInput legacy, ExecutionInput modern) => new(drivers, legacy, modern, string.Empty);

    public static ReplayPlan NotConstructible(string reason) => new(Drivers: null, new ExecutionInput([]), new ExecutionInput([]), reason);
}
