using System.Collections.Immutable;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// <c>packages.config</c> restore done by the tool itself (M3-029): every package a non-SDK project lists that is not
/// already in the repository folder is downloaded from the solution's sources and extracted into
/// <c>&lt;repositoryPath&gt;/&lt;Id&gt;.&lt;Version&gt;/</c>, the folder its HintPaths expect. No nuget.exe, Mono or
/// MSBuild; <c>dotnet msbuild -p:RestorePackagesConfig=true</c> restores nothing off Windows (M3-028). A package no
/// source has is reported, and the references that needed it then fail to resolve as they would on Windows.
/// </summary>
internal static class PackagesConfigRestorer
{
    public static async Task<ImmutableArray<LoadDiagnostic>> RestoreAsync(IEnumerable<string> projectPaths, NuGetSettings settings, PackageFeed feed, CancellationToken ct)
    {
        ImmutableArray<LoadDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<LoadDiagnostic>();
        foreach (string projectPath in projectPaths)
        {
            string directory = Path.GetDirectoryName(projectPath)!;
            string project = Path.GetFileNameWithoutExtension(projectPath);
            if ((ProjectPath.Resolve(directory, $"packages.{project}.config") ?? ProjectPath.Resolve(directory, "packages.config")) is not { } config)
            {
                continue;
            }

            foreach (XElement package in XDocument.Load(config).Root!.Elements().Where(static e => string.Equals(e.Name.LocalName, "package", StringComparison.Ordinal)))
            {
                string id = package.Attribute("id")?.Value ?? string.Empty;
                string version = package.Attribute("version")?.Value ?? string.Empty;
                string target = Path.Combine(settings.RepositoryPath, $"{id}.{version}");
                if (Directory.Exists(target))
                {
                    continue;
                }

                if (await feed.DownloadAsync(id, version, ct).ConfigureAwait(false) is { } bytes)
                {
                    PackageFeed.Extract(bytes, target, $"{id}.{version}.nupkg");
                }
                else
                {
                    diagnostics.Add(new LoadDiagnostic(
                        LoadDiagnosticKind.WorkspaceWarning,
                        string.Empty,
                        project,
                        $"packages.config restore: package '{id}' {version} was not found on any source ({string.Join(", ", feed.Sources)})"));
                }
            }
        }

        return diagnostics.ToImmutable();
    }
}
