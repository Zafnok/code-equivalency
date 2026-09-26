using System.Collections.Immutable;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The package sources and <c>packages.config</c> folder a solution's <c>nuget.config</c> files name (M3-029). Files are
/// read from the solution's directory up to the root, the nearest last, so the nearest wins: <c>&lt;clear/&gt;</c>,
/// <c>&lt;add&gt;</c> and <c>&lt;remove&gt;</c> under <c>packageSources</c>, <c>disabledPackageSources</c>, and
/// <c>repositoryPath</c> under <c>config</c>. With no source left, the source is nuget.org; with no
/// <c>repositoryPath</c>, packages go to <c>&lt;solution dir&gt;/packages</c>.
/// </summary>
internal sealed record NuGetSettings(ImmutableArray<string> Sources, string RepositoryPath)
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    public static NuGetSettings Read(string solutionDirectory)
    {
        List<string> files = [];
        for (DirectoryInfo? directory = new(solutionDirectory); directory is not null; directory = directory.Parent)
        {
            if (ProjectPath.Resolve(directory.FullName, "nuget.config") is { } file && File.Exists(file))
            {
                files.Insert(0, file);
            }
        }

        List<(string Key, string Value)> sources = [];
        HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
        string repositoryPath = Path.Combine(solutionDirectory, "packages");
        foreach (string file in files)
        {
            string directory = Path.GetDirectoryName(file)!;
            XElement root = XDocument.Load(file).Root!;
            foreach (XElement entry in Section(root, "packageSources"))
            {
                string key = entry.Attribute("key")?.Value ?? string.Empty;
                sources.RemoveAll(s => string.Equals(entry.Name.LocalName, "clear", StringComparison.Ordinal) || string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(entry.Name.LocalName, "add", StringComparison.Ordinal) && entry.Attribute("value")?.Value is { } value)
                {
                    sources.Add((key, IsRemote(value) ? value : ProjectPath.Full(directory, value)));
                }
            }

            foreach (XElement entry in Section(root, "disabledPackageSources").Where(static e => string.Equals(e.Name.LocalName, "add", StringComparison.Ordinal)))
            {
                string key = entry.Attribute("key")?.Value ?? string.Empty;
                if (string.Equals(entry.Attribute("value")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    disabled.Add(key);
                }
                else
                {
                    disabled.Remove(key);
                }
            }

            if (Section(root, "config").LastOrDefault(static e => string.Equals(e.Name.LocalName, "add", StringComparison.Ordinal) && string.Equals(e.Attribute("key")?.Value, "repositoryPath", StringComparison.OrdinalIgnoreCase)) is { } repository)
            {
                repositoryPath = ProjectPath.Full(directory, repository.Attribute("value")?.Value ?? string.Empty);
            }
        }

        ImmutableArray<string> enabled = [.. sources.Where(s => !disabled.Contains(s.Key)).Select(static s => s.Value)];
        return new NuGetSettings(enabled.IsEmpty ? [NuGetOrg] : enabled, repositoryPath);
    }

    public static bool IsRemote(string source) =>
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<XElement> Section(XElement root, string name) =>
        root.Elements().Where(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)).SelectMany(static s => s.Elements());
}
