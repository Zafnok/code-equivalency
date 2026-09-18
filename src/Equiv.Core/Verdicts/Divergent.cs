namespace Equiv.Core.Verdicts;

/// <summary>The two procedures disagree on some observable. SARIF EQ002 (or EQ006 for a runtime-changed callee).</summary>
public sealed record Divergent(Counterexample Counterexample) : Verdict;
