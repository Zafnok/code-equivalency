namespace LicenceCheck;

internal sealed record LicenceException(string PackageId, string Licence, string Reason);
