using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The projects a <c>.sln</c>'s default configuration builds (P2-013). A project with no <c>Build.0</c> entry for that
/// configuration is built neither by <c>dotnet build</c> nor by the solution restore, so it is not part of the product
/// and is not opened. The default configuration is <c>Debug|Any CPU</c>, else the first one the solution lists.
/// </summary>
internal static partial class SolutionBuildConfiguration
{
    private const string DefaultConfiguration = "Debug|Any CPU";
    private const string SolutionFolderType = "2150E333-8FDC-42A3-9474-1A3956D46DE8";

    /// <summary>
    /// The <c>.slnf</c> that opens only the built projects of the solution at <paramref name="solutionPath"/>, whose
    /// text is <paramref name="solutionText"/>, or null when every project is built (or none is, or there is no text,
    /// as for a <c>.slnx</c>): then the solution is opened as it is.
    /// </summary>
    public static SolutionFilter? Filter(string solutionPath, string? solutionText)
    {
        if (solutionText is null)
        {
            return null;
        }

        ImmutableArray<SolutionProject> projects = Projects(solutionText);
        HashSet<string> built = Built(solutionText);
        ImmutableArray<SolutionProject> kept = [.. projects.Where(p => built.Contains(p.Guid))];
        ImmutableArray<SolutionProject> notBuilt = [.. projects.Where(p => !built.Contains(p.Guid))];
        return kept.IsEmpty || notBuilt.IsEmpty
            ? null
            : new SolutionFilter(FilterJson(solutionPath, kept), [.. notBuilt.Select(static p => p.Name)]);
    }

    /// <summary>Every project entry that is not a solution folder.</summary>
    private static ImmutableArray<SolutionProject> Projects(string solutionText) =>
    [
        .. ProjectLine.Matches(solutionText)
            .Where(static m => !string.Equals(m.Groups["type"].Value, SolutionFolderType, StringComparison.OrdinalIgnoreCase))
            .Select(static m => new SolutionProject(m.Groups["name"].Value, m.Groups["path"].Value, m.Groups["guid"].Value)),
    ];

    /// <summary>
    /// The project GUIDs with a <c>Build.0</c> entry for the default configuration. With no solution configuration at
    /// all, every project counts as built.
    /// </summary>
    private static HashSet<string> Built(string solutionText)
    {
        ImmutableArray<string> configurations = [.. SolutionConfiguration.Matches(solutionText).Select(static m => m.Groups["configuration"].Value.Trim())];
        if (configurations.IsEmpty)
        {
            return [.. Projects(solutionText).Select(static p => p.Guid)];
        }

        string configuration = configurations.Contains(DefaultConfiguration, StringComparer.OrdinalIgnoreCase) ? DefaultConfiguration : configurations[0];
        return new(
            BuildEntry.Matches(solutionText)
                .Where(m => string.Equals(m.Groups["configuration"].Value.Trim(), configuration, StringComparison.OrdinalIgnoreCase))
                .Select(static m => m.Groups["guid"].Value),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A solution filter naming the solution and the projects to open, both as absolute paths.</summary>
    private static string FilterJson(string solutionPath, ImmutableArray<SolutionProject> projects)
    {
        string fullSolutionPath = Path.GetFullPath(solutionPath);
        string directory = Path.GetDirectoryName(fullSolutionPath)!;
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("solution");
            writer.WriteString("path", fullSolutionPath);
            writer.WriteStartArray("projects");
            foreach (SolutionProject project in projects)
            {
                writer.WriteStringValue(Path.GetFullPath(Path.Combine(directory, project.Path.Replace('\\', Path.DirectorySeparatorChar))));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed record SolutionProject(string Name, string Path, string Guid);

    [GeneratedRegex("""^\s*Project\("\{(?<type>[^}]*)\}"\)\s*=\s*"(?<name>[^"]*)"\s*,\s*"(?<path>[^"]*)"\s*,\s*"\{(?<guid>[^}]*)\}"\s*$""", RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectLine { get; }

    /// <summary>
    /// A <c>SolutionConfigurationPlatforms</c> line, which always maps a <c>Configuration|Platform</c> name to itself;
    /// no other line in a <c>.sln</c> has that shape.
    /// </summary>
    [GeneratedRegex(@"^\s*(?<configuration>[^=\r\n|]+\|[^=\r\n]+?)\s*=\s*\k<configuration>\s*$", RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SolutionConfiguration { get; }

    [GeneratedRegex(@"^\s*\{(?<guid>[^}]*)\}\.(?<configuration>[^\r\n=]*)\.Build\.0\s*=", RegexOptions.Multiline | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuildEntry { get; }
}
