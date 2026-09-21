namespace LicenceCheck;

/// <summary>
/// <paramref name="SamplesFullyRestored"/> is false when a direct samples/ PackageReference was
/// found but its nuspec was not on disk (samples/ is only restored under build.ps1 -Integration).
/// When that happens the resolved package set is a real subset of the truth, so a THIRD-PARTY-
/// NOTICES.md freshness check against it would be comparing against incomplete data, not
/// reporting a genuine drift.
/// </summary>
internal sealed record PackageInventory(IReadOnlySet<string> RedistributedIds, IReadOnlyList<ResolvedPackage> ExtraPackages, bool SamplesFullyRestored);
