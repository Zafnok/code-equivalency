namespace Equiv.Core.Execution;

/// <summary>
/// How to replay one Divergent's model (ADR 0035 decision 2; ticket M4-009): the two drivers, and the one case each side's
/// driver runs, in <see cref="ExecutionInput"/>'s wire form. When the model cannot become a call on both sides,
/// <see cref="Drivers"/> is null and <see cref="Reason"/> says why.
/// </summary>
public sealed record ReplayPlan(ExecutionDrivers? Drivers, ExecutionInput Legacy, ExecutionInput Modern, string Reason)
{
    /// <summary>
    /// Why equal real outcomes are no evidence against the model, or null when they are (ticket P2-037). It is set when the
    /// model's call traces differ: its outcomes may then rest on call answers the solver chose after the split, and a driver
    /// observes no trace. The replay is not constructible with this reason unless the real outcomes differ.
    /// </summary>
    public string? AlikeReason { get; init; }

    public static ReplayPlan Runnable(ExecutionDrivers drivers, ExecutionInput legacy, ExecutionInput modern) => new(drivers, legacy, modern, string.Empty);

    public static ReplayPlan NotConstructible(string reason) => new(Drivers: null, new ExecutionInput([]), new ExecutionInput([]), reason);
}
