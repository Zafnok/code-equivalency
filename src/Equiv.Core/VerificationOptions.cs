using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Progress;

namespace Equiv.Core;

/// <summary>
/// Per-run knobs for <see cref="IVerificationBackend"/> (ticket M3-001): the loop bound, the solver timeout, and the
/// call-identity unifications from <c>equiv.config.json</c>. <see cref="ChcIntMode"/> (<c>--chc-int-mode</c>, ticket
/// P1-001) lets rung 4 of the loop ladder encode bitvectors as integers once it has proved that sound; it is on unless
/// turned off. <see cref="InvariantModel"/> (<c>--invariant-model</c>, ticket P1-002) is the model rung 5 asks for a
/// coupling invariant when rung 4 times out; rung 5 is skipped when it is null, the default. <see cref="ResourceLimit"/>
/// (<c>--resource-limit</c>, ticket P2-050) is Z3's deterministic <c>rlimit</c> for each query, which is what bounds a
/// query on any machine; <see cref="TimeoutMs"/> is the wall-clock backstop behind it. <see cref="Solver"/> is the second
/// solver the backend asks a rung 1 query its own gave up on (ADR 0050; ticket P1-033), none when null, the default.
/// <see cref="LocalProposer"/> says whether rung 5's local proposer runs after a rung 4 timeout, and
/// <see cref="RefineTimeouts"/> whether a <c>timeout</c> Unknown is asked ADR 0037's two queries; both are on unless a
/// pass of <c>equiv compare</c> turns them off, which the first pass does when a budget pass follows and quick mode does
/// throughout (ADR 0049; ticket P1-032). Neither changes what a verdict claims.
/// <see cref="Log"/> is where the backend reports its progress (ADR 0038); it is not part of equality, since it never
/// changes a verdict.
/// </summary>
public sealed record VerificationOptions(int Bound, int TimeoutMs, ImmutableDictionary<string, string> CallIdentityMap)
{
    public bool ChcIntMode { get; init; } = true;

    public string? InvariantModel { get; init; }

    public int ResourceLimit { get; init; } = EquivConfig.DefaultResourceLimit;

    public ISmtSolver? Solver { get; init; }

    public bool LocalProposer { get; init; } = true;

    public bool RefineTimeouts { get; init; } = true;

    public IRunLog Log { get; init; } = NullRunLog.Instance;

    // Deliberate non-short-circuit '&': see the comment on Equiv.Core.Configuration.EquivConfig.Equals.
    public bool Equals(VerificationOptions? other) =>
        other is not null
        && (Bound == other.Bound)
            & (TimeoutMs == other.TimeoutMs) // NOSONAR
            & ConfigEquality.DictionaryEqual(CallIdentityMap, other.CallIdentityMap) // NOSONAR
            & (ChcIntMode == other.ChcIntMode) // NOSONAR
            & string.Equals(InvariantModel, other.InvariantModel, StringComparison.Ordinal) // NOSONAR
            & (ResourceLimit == other.ResourceLimit) // NOSONAR
            & Equals(Solver, other.Solver) // NOSONAR
            & (LocalProposer == other.LocalProposer) // NOSONAR
            & (RefineTimeouts == other.RefineTimeouts); // NOSONAR

    public override int GetHashCode() => HashCode.Combine(Bound, TimeoutMs, ConfigEquality.Hash(CallIdentityMap), ChcIntMode, InvariantModel, ResourceLimit, Solver, HashCode.Combine(LocalProposer, RefineTimeouts));
}
