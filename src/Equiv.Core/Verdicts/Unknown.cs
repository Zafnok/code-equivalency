using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// <see cref="Reason"/> plus a human-readable <see cref="Detail"/> (the solver's message, the opaque
/// node's reason, the ambiguous overload's candidates, ...). SARIF EQ003. <see cref="Causes"/> are the lines it points
/// at, each reached opaque node and each abstraction it depends on (ADR 0027 decision 4). For
/// <see cref="UnknownReason.Abstraction"/>, <see cref="Candidate"/> is the solver's model replayed on both sides, and
/// <see cref="Abstractions"/> are what its divergence depends on (ADR 0026). None of the three is part of the result's
/// fingerprint.
/// </summary>
public sealed record Unknown(UnknownReason Reason, string Detail) : Verdict
{
    /// <summary>
    /// The <c>IrOpaque</c> reason a frontend gives a method whose bound body is erroneous; a pair with one on
    /// either side is <see cref="UnknownReason.Unbound"/> (ADR 0029 decision 2).
    /// </summary>
    public const string UnboundOpaqueReason = "unbound";

    public ImmutableArray<UnknownCause> Causes { get; init; } = [];

    public Counterexample? Candidate { get; init; }

    public ImmutableArray<Abstraction> Abstractions { get; init; } = [];

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
            & (Candidate == other.Candidate)
            & IrEquality.SequenceEqual(Abstractions, other.Abstractions);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), Reason, Detail, IrEquality.Hash(Causes), Candidate, IrEquality.Hash(Abstractions));
}
