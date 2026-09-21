namespace LicenceCheck;

internal sealed record PackageInventory(IReadOnlySet<string> RedistributedIds, IReadOnlyList<ResolvedPackage> ExtraPackages);
