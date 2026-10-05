namespace Equiv.Core.Verdicts;

/// <summary>
/// What the backend's search for an input condition found on a pair that is not Equivalent (ADR 0048; ticket P1-022):
/// <see cref="AgreesWhen"/>, the disjunction of the candidates it admitted, or null when it admitted none.
/// <see cref="Contradicted"/> says the result's own counterexample satisfied the admitted condition, which no proof allows:
/// a bug in the tool, so <see cref="AgreesWhen"/> is then null and the run carries a warning. <see cref="Elapsed"/> is the
/// time the search took, which the census sums; it is a measurement, not a result, so equality ignores it. None of it is
/// part of the result's fingerprint.
/// </summary>
public sealed record ConditionSearch(AgreesWhen? AgreesWhen)
{
    public bool Contradicted { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>The search a verdict carries: a <see cref="Divergent"/>'s or an <see cref="Unknown"/>'s, else null.</summary>
    public static ConditionSearch? Of(Verdict verdict) => verdict switch
    {
        Divergent divergent => divergent.Conditions,
        Unknown unknown => unknown.Conditions,
        _ => null,
    };

    public bool Equals(ConditionSearch? other) =>
        other is not null && AgreesWhen == other.AgreesWhen && Contradicted == other.Contradicted;

    public override int GetHashCode() => HashCode.Combine(AgreesWhen, Contradicted);
}
