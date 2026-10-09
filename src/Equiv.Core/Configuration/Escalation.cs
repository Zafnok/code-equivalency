namespace Equiv.Core.Configuration;

/// <summary>
/// The budgets of thorough mode's budget pass (ADR 0049 decision 2; ticket P1-032): the config key <c>escalation</c>,
/// whose three keys are the first pass's <c>bound</c>, <c>resourceLimit</c> and <c>timeoutMs</c> for the pairs that pass
/// verifies again.
/// </summary>
public sealed record Escalation(int Bound, int ResourceLimit, int TimeoutMs)
{
    /// <summary>
    /// ADR 0049's table as its clarification of 2026-10-08 reads (ticket P2-134): the largest budget measured
    /// (<c>docs/runs/2026-10-01-timeout-budget.md</c>), and the first pass's bound. A bound of 8 took 2.5 times as long on
    /// <c>gitextensions-8522</c> and no divergence it found needed more than three iterations (<c>docs/runs/2026-10-08-thorough-budgets.md</c>).
    /// </summary>
    public static Escalation Default { get; } = new(Bound: 3, ResourceLimit: 30_000_000, TimeoutMs: 600_000);

    /// <summary>
    /// These budgets, none below the first pass's (ADR 0049 decision 4): a config that sets a large <c>resourceLimit</c>
    /// is never asked again with less.
    /// </summary>
    public Escalation AtLeast(int bound, int resourceLimit, int timeoutMs) =>
        new(Math.Max(Bound, bound), Math.Max(ResourceLimit, resourceLimit), Math.Max(TimeoutMs, timeoutMs));
}
