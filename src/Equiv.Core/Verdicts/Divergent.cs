using Equiv.Core.Execution;
using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// The two procedures disagree on some observable. SARIF EQ002 (or EQ006 for a runtime-changed callee). A solver's
/// Divergent carries its model as <see cref="Counterexample"/>. One the real runtimes showed while an Unknown pair was
/// tested carries <see cref="Observed"/> instead (<c>proofMethod: observed</c>; ADR 0035 decision 3, ticket P1-008): it
/// has no model of the IR, so its <see cref="Counterexample"/> is <see cref="Unmodelled"/> and is never shown.
/// </summary>
public sealed record Divergent(Counterexample Counterexample) : Verdict
{
    /// <summary>The counterexample of an observed divergence: no inputs, and neither run modelled.</summary>
    public static Counterexample Unmodelled { get; } = new(new IrInputs([]), new IrRun(new IrInfeasible(), [], []), new IrRun(new IrInfeasible(), [], []));

    public ObservedDivergence? Observed { get; init; }

    /// <summary>
    /// The backend's search for an input condition under which the pair is Equivalent (ADR 0048; ticket P1-022); null for
    /// a Divergent it did not search, such as one on a looping pair or an observed one. Not part of the fingerprint.
    /// </summary>
    public ConditionSearch? Conditions { get; init; }

    /// <summary>The divergence <paramref name="observed"/> showed on the real runtimes.</summary>
    public static Divergent Observation(ObservedDivergence observed) => new(Unmodelled) { Observed = observed };
}
