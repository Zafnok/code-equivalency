using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// An input the two procedures disagree on, and each side's run (VERIFICATION-MODEL.md section 5;
/// shape from ticket M3-001's model decoder). <see cref="IrRun"/> already bundles outcome, final
/// by-ref values and the call trace, so no separate outcome/trace fields are needed here.
/// </summary>
public sealed record Counterexample(IrInputs Inputs, IrRun Old, IrRun New)
{
    /// <summary>
    /// The model's answers to the runs' calls of a delegate input (ticket P2-249), what a replay builds that delegate from.
    /// They are read off the model the runs already are, so they take no part in equality, and the counterexample's text
    /// and fingerprint do not show them.
    /// </summary>
    public ImmutableArray<CallAnswer> Answers { get; init; } = [];

    public bool Equals(Counterexample? other) => other is not null && (Inputs == other.Inputs) & (Old == other.Old) & (New == other.New);

    public override int GetHashCode() => HashCode.Combine(Inputs, Old, New);
}
