namespace LicenceCheck;

internal sealed record LicenceViolation(string PackageId, string Version, string Message)
{
    public override string ToString() => $"{PackageId} {Version}: {Message}";
}
