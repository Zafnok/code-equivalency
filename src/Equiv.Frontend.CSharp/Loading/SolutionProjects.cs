using System.Collections.Immutable;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The projects a solution builds (M3-029), as full paths, and the names of those its default configuration does not
/// build: from <see cref="SolutionBuildConfiguration"/> for a <c>.sln</c>, and every <c>&lt;Project Path&gt;</c> for a
/// <c>.slnx</c>, which is always opened whole (P2-013).
/// </summary>
internal sealed record SolutionProjects(ImmutableArray<string> Built, ImmutableArray<string> NotBuilt)
{
    public static SolutionProjects Read(string solutionPath)
    {
        string text = File.ReadAllText(solutionPath);
        if (!solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            (ImmutableArray<string> built, ImmutableArray<string> notBuilt) = SolutionBuildConfiguration.Partition(solutionPath, text);
            return new SolutionProjects(built, notBuilt);
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        return new SolutionProjects(
            [
                .. XDocument.Parse(text).Descendants()
                    .Where(static e => string.Equals(e.Name.LocalName, "Project", StringComparison.Ordinal) && e.Attribute("Path") is not null)
                    .Select(e => ProjectPath.Full(directory, e.Attribute("Path")!.Value)),
            ],
            []);
    }
}
