namespace LicenceCheck;

/// <summary>
/// A package as found in a lock file, a local tool manifest, or a samples/ direct reference,
/// together with whatever its own nuspec exposes about its licence.
/// </summary>
internal sealed record ResolvedPackage(
    string Id,
    string Version,
    PackageRole Role,
    string DeclaredLicence,
    bool IsSpdxExpression);
