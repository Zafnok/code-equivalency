using System.Xml.Linq;

namespace LicenceCheck;

/// <summary>
/// Reads a restored package's own <c>.nuspec</c> from the local NuGet cache. Used for the two
/// sources <c>nuget-license</c> cannot reach: local tools (<c>.config/dotnet-tools.json</c>) and
/// direct samples/ references (no lock file, isolated from the root build). Package cache layout
/// per CLAUDE.md: <c>&lt;cache&gt;/&lt;id lower-cased&gt;/&lt;version&gt;/&lt;id lower-cased&gt;.nuspec</c>.
/// </summary>
internal static class NuspecLicenseReader
{
    public static (string Licence, bool IsSpdxExpression) Read(string packageId, string version, string nugetPackagesRoot)
    {
        string idLower = packageId.ToLowerInvariant();
        string nuspecPath = Path.Combine(nugetPackagesRoot, idLower, version, $"{idLower}.nuspec");
        if (!File.Exists(nuspecPath))
        {
            throw new LicenceCheckException($"could not find a restored nuspec for {packageId} {version} at '{nuspecPath}'. Was it restored (dotnet restore / dotnet tool restore)?");
        }

        XDocument document = XDocument.Load(nuspecPath);
        XElement root = document.Root ?? throw new LicenceCheckException($"'{nuspecPath}' has no root element.");
        XNamespace ns = root.Name.Namespace;
        XElement? metadata = root.Element(ns + "metadata");

        XElement? licenseElement = metadata?.Element(ns + "license");
        if (licenseElement is not null)
        {
            string type = licenseElement.Attribute("type")?.Value ?? string.Empty;
            string value = licenseElement.Value.Trim();
            return (value, string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase));
        }

        string? licenseUrl = metadata?.Element(ns + "licenseUrl")?.Value.Trim();
        return (licenseUrl is { Length: > 0 } ? licenseUrl : "(nuspec has no <license> or <licenseUrl>)", false);
    }
}
