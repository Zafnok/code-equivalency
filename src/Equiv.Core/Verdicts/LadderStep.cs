using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// One rung the loop ladder attempted, its outcome and a human-readable detail. SARIF <c>properties.ladderTrace</c>.
/// <see cref="Mode"/> is the theory rung 4 ran in, when it ran (ticket P1-001), SARIF <c>properties.chcMode</c>.
/// <see cref="Solver"/> is the second solver that answered one of the rung's queries, when one did (ADR 0050 decision 4;
/// ticket P1-033): the step's <c>solver</c> in <c>ladderTrace</c>, and the <c>+cvc5</c> suffix of <c>proofMethod</c>.
/// <see cref="Refined"/> are the pure functions a refined round of rung 1 gave their real meaning, sorted (ADR 0053
/// decision 8; ticket P1-030), and empty for every other step: the <c>+refined</c> suffix of <c>proofMethod</c>, and
/// SARIF <c>properties.refined</c>.
/// </summary>
public sealed record LadderStep(ProofMethod Rung, RungOutcome Outcome, string Detail)
{
    public ChcMode? Mode { get; init; }

    public SolverUse? Solver { get; init; }

    public ImmutableArray<string> Refined { get; init; } = [];

    public bool Equals(LadderStep? other) =>
        other is not null
        && (Rung == other.Rung)
            & (Outcome == other.Outcome)
            & string.Equals(Detail, other.Detail, StringComparison.Ordinal)
            & (Mode == other.Mode)
            & (Solver == other.Solver)
            & IrEquality.SequenceEqual(Refined, other.Refined);

    public override int GetHashCode() => HashCode.Combine(Rung, Outcome, Detail, Mode, Solver, IrEquality.Hash(Refined));
}
