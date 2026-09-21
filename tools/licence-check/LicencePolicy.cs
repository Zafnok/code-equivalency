namespace LicenceCheck;

internal sealed record LicencePolicy(IReadOnlyList<string> AllowedLicenses, IReadOnlyList<LicenceException> Exceptions)
{
    public bool IsAllowed(string licence) => AllowedLicenses.Contains(licence, StringComparer.Ordinal);

    public LicenceException? FindException(string packageId) =>
        Exceptions.FirstOrDefault(exception => string.Equals(exception.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
}
