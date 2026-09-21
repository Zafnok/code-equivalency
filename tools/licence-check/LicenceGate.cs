namespace LicenceCheck;

/// <summary>
/// Pure policy evaluation: no file or process I/O. A package passes if a policy exception names
/// it (the exception's own licence is used, whether or not that licence is itself allowed) or if
/// its nuspec exposed a clean SPDX expression that is on the allowlist. Everything else fails the
/// gate (ticket M0-010, AC3 and AC5).
/// </summary>
internal static class LicenceGate
{
    public static LicenceGateResult Evaluate(IReadOnlyList<ResolvedPackage> packages, LicencePolicy policy)
    {
        List<ResolvedPackage> passed = [];
        List<LicenceViolation> violations = [];

        foreach (ResolvedPackage package in packages)
        {
            LicenceException? exception = policy.FindException(package.Id);
            if (exception is not null)
            {
                passed.Add(package with { DeclaredLicence = exception.Licence });
                continue;
            }

            if (package.IsSpdxExpression && policy.IsAllowed(package.DeclaredLicence))
            {
                passed.Add(package);
                continue;
            }

            string message = package.IsSpdxExpression
                ? $"licence '{package.DeclaredLicence}' is not on the allowlist and no policy exception permits it."
                : $"licence could not be determined (nuspec did not expose an SPDX expression; found '{package.DeclaredLicence}') and no policy exception declares it.";
            violations.Add(new LicenceViolation(package.Id, package.Version, message));
        }

        return new LicenceGateResult(passed, violations);
    }
}
