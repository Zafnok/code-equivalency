using System.Text.Json;
using System.Xml.Linq;

namespace LicenceCheck;

/// <summary>
/// Builds the two things <see cref="NuGetLicenseInvoker"/> and the gate need beyond what
/// <c>nuget-license</c> itself can see: which package ids in the main solution's lock files are
/// actually redistributed (src/, minus whatever <c>Directory.Build.props</c> marks
/// <c>PrivateAssets="All"</c> for every project), and the packages that have no lock file at all
/// (local tools from <c>.config/dotnet-tools.json</c>, direct samples/ references), resolved by
/// reading their nuspec directly.
/// </summary>
internal static class PackageInventoryBuilder
{
    public static PackageInventory Build(string repoRoot, string nugetPackagesRoot)
    {
        IReadOnlySet<string> privateAssetsAll = ReadPrivateAssetsAll(Path.Combine(repoRoot, "Directory.Build.props"));
        IReadOnlySet<string> redistributedIds = ReadRedistributedIds(Path.Combine(repoRoot, "src"), privateAssetsAll);

        List<ResolvedPackage> extra = [];
        extra.AddRange(ReadLocalTools(repoRoot, nugetPackagesRoot));
        extra.AddRange(ReadSamplesDirectReferences(repoRoot, nugetPackagesRoot, redistributedIds));

        return new PackageInventory(redistributedIds, extra);
    }

    private static HashSet<string> ReadPrivateAssetsAll(string directoryBuildPropsPath)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        XDocument document = XDocument.Load(directoryBuildPropsPath);
        foreach (XElement reference in PackageReferences(document))
        {
            if (string.Equals(reference.Attribute("PrivateAssets")?.Value, "All", StringComparison.OrdinalIgnoreCase))
            {
                string? id = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static HashSet<string> ReadRedistributedIds(string srcDirectory, IReadOnlySet<string> privateAssetsAll)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string lockFilePath in Directory.EnumerateFiles(srcDirectory, "packages.lock.json", SearchOption.AllDirectories))
        {
            foreach (string id in ReadLockFilePackageIds(lockFilePath))
            {
                ids.Add(id);
            }
        }

        ids.ExceptWith(privateAssetsAll);
        return ids;
    }

    private static List<string> ReadLockFilePackageIds(string lockFilePath)
    {
        List<string> ids = [];
        using FileStream stream = File.OpenRead(lockFilePath);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("dependencies", out JsonElement dependencies))
        {
            return ids;
        }

        foreach (JsonProperty targetFramework in dependencies.EnumerateObject())
        {
            foreach (JsonProperty package in targetFramework.Value.EnumerateObject())
            {
                ids.Add(package.Name);
            }
        }

        return ids;
    }

    private static List<ResolvedPackage> ReadLocalTools(string repoRoot, string nugetPackagesRoot)
    {
        List<ResolvedPackage> packages = [];
        string manifestPath = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        using FileStream stream = File.OpenRead(manifestPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("tools", out JsonElement tools))
        {
            return packages;
        }

        foreach (JsonProperty tool in tools.EnumerateObject())
        {
            string id = tool.Name;
            string version = tool.Value.GetProperty("version").GetString()!;
            (string licence, bool isSpdxExpression) = NuspecLicenseReader.Read(id, version, nugetPackagesRoot);
            packages.Add(new ResolvedPackage(id, version, PackageRole.BuildAndTestOnly, licence, isSpdxExpression));
        }

        return packages;
    }

    private static List<ResolvedPackage> ReadSamplesDirectReferences(string repoRoot, string nugetPackagesRoot, IReadOnlySet<string> redistributedIds)
    {
        List<ResolvedPackage> packages = [];
        string samplesDirectory = Path.Combine(repoRoot, "samples");
        if (!Directory.Exists(samplesDirectory))
        {
            return packages;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string csprojPath in Directory.EnumerateFiles(samplesDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            if (csprojPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || csprojPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument document = XDocument.Load(csprojPath);
            foreach (XElement reference in PackageReferences(document))
            {
                string? id = reference.Attribute("Include")?.Value;
                string? version = reference.Attribute("Version")?.Value;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version) || redistributedIds.Contains(id) || !seen.Add(id))
                {
                    continue;
                }

                // samples/ is only restored under -Integration (the legacy side needs MSBuild.exe,
                // Windows-only); a plain ./build.ps1 run on ubuntu-latest never restores it, so a
                // missing nuspec here means "not part of this run", not an undetermined licence.
                if (!NuspecLicenseReader.IsRestored(id, version, nugetPackagesRoot))
                {
                    continue;
                }

                (string licence, bool isSpdxExpression) = NuspecLicenseReader.Read(id, version, nugetPackagesRoot);
                packages.Add(new ResolvedPackage(id, version, PackageRole.SamplesOnly, licence, isSpdxExpression));
            }
        }

        return packages;
    }

    /// <summary>
    /// &lt;PackageReference&gt; elements regardless of XML namespace: legacy (non-SDK) csproj like
    /// samples/webapi-basic/legacy declare the MSBuild 2003 xmlns, SDK-style csproj and
    /// Directory.Build.props declare none. <see cref="XContainer.Descendants(XName)"/> matches by
    /// exact qualified name, so a plain "PackageReference" lookup silently misses the namespaced ones.
    /// </summary>
    private static IEnumerable<XElement> PackageReferences(XDocument document) =>
        document.Descendants().Where(static element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.Ordinal));
}
