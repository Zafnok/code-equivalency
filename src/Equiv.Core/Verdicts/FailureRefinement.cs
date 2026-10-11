namespace Equiv.Core.Verdicts;

/// <summary>
/// ADR 0037's two queries on an Unknown or a Divergent pair (tickets P1-013 and P1-020): <see cref="NewFailures"/> asks for
/// an input on which the legacy side returns normally and the modern side throws, <see cref="RemovedFailures"/> the same
/// with the sides swapped. Only the throw observables are compared. A Divergent's also carries
/// <see cref="ResultChanges"/>, the third query, and from the three its <see cref="Kind"/>. <see cref="Elapsed"/> is the
/// time the queries took, which the census sums; it is a measurement, not a result, so equality ignores it. None of it is
/// part of the result's fingerprint.
/// </summary>
public sealed record FailureRefinement(RefinementResult NewFailures, RefinementResult RemovedFailures)
{
    /// <summary>
    /// Whether some input makes both sides return, or both throw the same exception type, with another compared observable
    /// differing; null on an Unknown, which is not asked.
    /// </summary>
    public RefinementResult? ResultChanges { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>The kind the three answers give; null without <see cref="ResultChanges"/>.</summary>
    public DivergenceKind? Kind => ResultChanges is null
        ? null
        : (NewFailures.Outcome, RemovedFailures.Outcome, ResultChanges.Outcome) switch
        {
            (_, _, RefinementOutcome.Found) => DivergenceKind.ResultChanged,
            (RefinementOutcome.NoneProved, RefinementOutcome.Found, RefinementOutcome.NoneProved) => DivergenceKind.RemovedFailuresOnly,
            (RefinementOutcome.Found, RefinementOutcome.NoneProved, RefinementOutcome.NoneProved) => DivergenceKind.NewFailuresOnly,
            (RefinementOutcome.Found, RefinementOutcome.Found, RefinementOutcome.NoneProved) => DivergenceKind.FailuresOnly,
            _ => DivergenceKind.Undetermined,
        };

    public bool Equals(FailureRefinement? other) =>
        other is not null && (NewFailures == other.NewFailures) & (RemovedFailures == other.RemovedFailures) & (ResultChanges == other.ResultChanges);

    public override int GetHashCode() => HashCode.Combine(NewFailures, RemovedFailures, ResultChanges);
}
