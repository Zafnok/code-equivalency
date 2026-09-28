namespace Equiv.Core.Verdicts;

/// <summary>
/// ADR 0037's two queries on an Unknown pair (ticket P1-013): <see cref="NewFailures"/> asks for an input on which the
/// legacy side returns normally and the modern side throws, <see cref="RemovedFailures"/> the same with the sides swapped.
/// Only the throw observables are compared. <see cref="Elapsed"/> is the time both queries took, which the census sums; it
/// is a measurement, not a result, so equality ignores it. None of it is part of the result's fingerprint.
/// </summary>
public sealed record FailureRefinement(RefinementResult NewFailures, RefinementResult RemovedFailures)
{
    public TimeSpan Elapsed { get; init; }

    public bool Equals(FailureRefinement? other) =>
        other is not null && NewFailures == other.NewFailures && RemovedFailures == other.RemovedFailures;

    public override int GetHashCode() => HashCode.Combine(NewFailures, RemovedFailures);
}
