namespace LicenceCheck;

internal sealed record LicenceGateResult(IReadOnlyList<ResolvedPackage> Passed, IReadOnlyList<LicenceViolation> Violations)
{
    public bool Success => Violations.Count == 0;
}
