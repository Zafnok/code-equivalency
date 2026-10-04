using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// <see cref="Reason"/> plus a human-readable <see cref="Detail"/> (the solver's message, the opaque
/// node's reason, the ambiguous overload's candidates, ...). SARIF EQ003. <see cref="Causes"/> are the lines it points
/// at, each reached opaque node and each abstraction it depends on (ADR 0027 decision 4). For
/// <see cref="UnknownReason.Abstraction"/>, <see cref="Candidate"/> is the solver's model replayed on both sides, and
/// <see cref="Abstractions"/> are what its divergence depends on (ADR 0026). None of the three is part of the result's
/// fingerprint, and neither is <see cref="Scope"/>, which is <see cref="UnknownScope.Method"/> unless a backend proved the
/// residual claim (ADR 0029 decision 4).
/// </summary>
public sealed record Unknown(UnknownReason Reason, string Detail) : Verdict
{
    /// <summary>What a <see cref="UnknownScope.Line"/> Unknown still proves, as SARIF's <c>properties.residualClaim</c>.</summary>
    public const string ResidualClaim = "equivalent unless a relatedLocation is reached";

    /// <summary>
    /// The <c>IrOpaque</c> reason a frontend gives a method whose bound body is erroneous; a pair with one on
    /// either side is <see cref="UnknownReason.Unbound"/> (ADR 0029 decision 2).
    /// </summary>
    public const string UnboundOpaqueReason = "unbound";

    /// <summary>
    /// The detail of a pair where exactly one side is <c>async</c>, whose exception timing differs (ticket M4-006), and the
    /// reason of the whole-body <c>IrOpaque</c> a frontend gives both its bodies: <see cref="UnknownReason.Opaque"/>, decided
    /// without the solver.
    /// </summary>
    public const string AsyncMismatchReason = "async-mismatch";

    public ImmutableArray<UnknownCause> Causes { get; init; } = [];

    public UnknownScope Scope { get; init; }

    public Counterexample? Candidate { get; init; }

    public ImmutableArray<Abstraction> Abstractions { get; init; } = [];

    /// <summary>
    /// Whether the modern side can fail where the legacy side does not, and the reverse (ADR 0037; ticket P1-013); null for
    /// an Unknown the backend did not query, such as <see cref="UnknownReason.Unbound"/> and <see cref="UnknownReason.Timeout"/>.
    /// Not part of the fingerprint.
    /// </summary>
    public FailureRefinement? FailureRefinement { get; init; }

    /// <summary>
    /// The backend's search for an input condition under which the pair is Equivalent (ADR 0048; ticket P1-022); null for
    /// an Unknown it did not search, which is every reason but <see cref="UnknownReason.Abstraction"/> and every looping
    /// pair. Not part of the fingerprint.
    /// </summary>
    public ConditionSearch? Conditions { get; init; }

    /// <summary>
    /// A divergence the replay found only in tainted observables (ADR 0026): <see cref="UnknownReason.Abstraction"/>,
    /// whose detail names the tainting identities, with <paramref name="candidate"/> and <paramref name="abstractions"/>
    /// attached, and each abstraction with a span as a cause.
    /// </summary>
    public static Unknown DependingOn(Counterexample candidate, ImmutableArray<Abstraction> abstractions) =>
        new(UnknownReason.Abstraction, $"the divergence depends on {string.Join(", ", abstractions.Select(static a => a.Identity.Value).Distinct(StringComparer.Ordinal))}")
        {
            Candidate = candidate,
            Abstractions = abstractions,
            Causes = [.. abstractions.Select(static a => a.Cause).OfType<UnknownCause>()],
        };

    public bool Equals(Unknown? other) =>
        other is not null
        && base.Equals(other)
        && (Reason == other.Reason)
            & string.Equals(Detail, other.Detail, StringComparison.Ordinal)
            & IrEquality.SequenceEqual(Causes, other.Causes)
            & (Scope == other.Scope)
            & (Candidate == other.Candidate)
            & IrEquality.SequenceEqual(Abstractions, other.Abstractions)
            & (FailureRefinement == other.FailureRefinement)
            & (Conditions == other.Conditions);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), Reason, Detail, IrEquality.Hash(Causes), Scope, HashCode.Combine(Candidate, IrEquality.Hash(Abstractions), FailureRefinement, Conditions));
}
