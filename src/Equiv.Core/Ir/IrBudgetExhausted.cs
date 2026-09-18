namespace Equiv.Core.Ir;

/// <summary>The step budget ran out first (a loop ran longer than the budget allows).</summary>
public sealed record IrBudgetExhausted : IrOutcome;
