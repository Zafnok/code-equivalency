using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The loader off Windows (M3-029, ADR 0031 as clarified by M3-028). The projects the solution builds
/// (<see cref="SolutionProjects"/>) are split by <see cref="SdkStyleProject"/>. SDK-style projects load first, through
/// MSBuildWorkspace on the .NET SDK (<see cref="MsBuildSolutionLoader"/>), from a <c>.slnf</c> that leaves the non-SDK
/// ones out when there are any. Non-SDK C# projects then load through the bare loader
/// (<see cref="BareProjectLoader"/>), after their <c>packages.config</c> packages are restored
/// (<see cref="PackagesConfigRestorer"/>); a non-SDK project that references an SDK-style one binds against its
/// compilation. Last, SDK-style compilations are rebound against the bare compilations of the non-SDK projects they
/// reference (<see cref="BareReferenceRebinder"/>), and any copy MSBuildWorkspace opened of a non-SDK project is
/// dropped. The result is one <see cref="LoadedSolution"/>; its <see cref="LoadedSolution.Solution"/> holds the
/// SDK-style projects only, since nothing downstream reads it.
/// </summary>
internal sealed class CompositeSolutionLoader : ISolutionLoader
{
    private readonly ISolutionLoader _sdkStyleLoader;
    private readonly Func<string, string?> _environment;
    private readonly Func<Uri, CancellationToken, Task<byte[]?>> _get;

    public CompositeSolutionLoader()
        : this(new MsBuildSolutionLoader(), Environment.GetEnvironmentVariable, HttpPackageSource.GetAsync)
    {
    }

    /// <summary>Seam for unit tests: the SDK-style loader, the environment, and the HTTP GET behind the package feed.</summary>
    internal CompositeSolutionLoader(ISolutionLoader sdkStyleLoader, Func<string, string?> environment, Func<Uri, CancellationToken, Task<byte[]?>> get)
    {
        _sdkStyleLoader = sdkStyleLoader;
        _environment = environment;
        _get = get;
    }

    public async Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        string directory = Path.GetDirectoryName(fullPath)!;
        SolutionProjects projects = SolutionProjects.Read(fullPath);
        ImmutableArray<string> sdkStyle = [.. projects.Built.Where(p => IsSdkStyle(directory, p))];
        ImmutableArray<string> nonSdk = [.. projects.Built.Where(p => !IsSdkStyle(directory, p)).Select(p => ProjectPath.Resolve(directory, p) ?? p)];

        LoadedSolution sdk = sdkStyle.IsEmpty
            ? new LoadedSolution(EmptySolution(), [], [], [])
            : await LoadSdkStyleAsync(fullPath, sdkStyle, whole: nonSdk.IsEmpty, ct).ConfigureAwait(false);
        (HashSet<string> dropped, ImmutableArray<string> droppedPaths, Dictionary<string, string> nameByAssembly, Dictionary<string, Compilation> sdkByPath) = Opened(sdk, directory);

        NuGetSettings settings = NuGetSettings.Read(directory);
        PackageFeed feed = new(settings.Sources, _get);
        ImmutableArray<LoadDiagnostic> restore = await PackagesConfigRestorer.RestoreAsync(nonSdk, settings, feed, ct).ConfigureAwait(false);
        BareProjectLoader bare = new(
            _environment,
            new ReferenceAssemblyCache(Path.GetFullPath(ReferenceAssemblyCache.DefaultRoot(_environment))),
            feed,
            NetStandardShims.Find(_environment),
            sdkByPath);
        foreach (string project in nonSdk.Concat(droppedPaths))
        {
            await bare.LoadAsync(project, ct).ConfigureAwait(false);
        }

        List<Compilation> compilations = [];
        List<LoadDiagnostic> diagnostics = [.. sdk.Diagnostics.Where(d => !dropped.Contains(d.Project)), .. restore];
        List<SkippedProject> skipped = [.. sdk.Skipped.Where(s => !dropped.Contains(s.Name))];
        Rebind(sdk.Compilations.Where(c => nameByAssembly.ContainsKey(c.AssemblyName!)), bare.Projects, nameByAssembly, (compilations, diagnostics, skipped), ct);
        foreach (BareProject project in bare.Projects)
        {
            if (project.IsSkipped)
            {
                skipped.Add(project.ToSkipped());
            }
            else
            {
                compilations.Add(project.Compilation!);
                diagnostics.AddRange(project.Diagnostics);
            }
        }

        return compilations.Count > 0
            ? new LoadedSolution(sdk.Solution, [.. compilations], [.. diagnostics], [.. skipped]) { NotBuilt = projects.NotBuilt }
            : throw new SolutionLoadException(
                solutionPath,
                [new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, string.Empty, "the solution contains no C# project that loads"), .. skipped.SelectMany(static s => s.Diagnostics)]);
    }

    /// <summary>
    /// What MSBuildWorkspace opened: the names and paths of the non-SDK projects it opened anyway, as targets of an
    /// SDK-style project's reference, which are dropped and loaded by the bare loader instead; each SDK-style project's
    /// name by assembly name; and each SDK-style project's compilation by path, for non-SDK projects that reference it.
    /// </summary>
    private static (HashSet<string> Dropped, ImmutableArray<string> DroppedPaths, Dictionary<string, string> NameByAssembly, Dictionary<string, Compilation> ByPath) Opened(LoadedSolution sdk, string directory)
    {
        HashSet<string> dropped = new(StringComparer.Ordinal);
        ImmutableArray<string>.Builder droppedPaths = ImmutableArray.CreateBuilder<string>();
        Dictionary<string, string> nameByAssembly = new(StringComparer.Ordinal);
        Dictionary<string, Compilation> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in sdk.Solution.Projects)
        {
            if (!IsSdkStyle(directory, project.FilePath!))
            {
                dropped.Add(project.Name);
                droppedPaths.Add(Path.GetFullPath(project.FilePath!));
                continue;
            }

            nameByAssembly.TryAdd(project.AssemblyName, project.Name);
            if ((sdk.Compilations.FirstOrDefault(c => string.Equals(c.AssemblyName, project.AssemblyName, StringComparison.Ordinal))
                ?? sdk.Skipped.FirstOrDefault(s => string.Equals(s.AssemblyName, project.AssemblyName, StringComparison.Ordinal))?.Compilation) is { } compilation)
            {
                byPath[Path.GetFullPath(project.FilePath!)] = compilation;
            }
        }

        return (dropped, droppedPaths.ToImmutable(), nameByAssembly, byPath);
    }

    /// <summary>
    /// Adds each SDK-style compilation to <paramref name="result"/>, rebound against the bare compilations. One whose
    /// references changed has its compiler errors recomputed and is skipped if a reference is now unresolved.
    /// </summary>
    private static void Rebind(
        IEnumerable<Compilation> sdkStyle,
        IReadOnlyList<BareProject> bare,
        Dictionary<string, string> nameByAssembly,
        (List<Compilation> Compilations, List<LoadDiagnostic> Diagnostics, List<SkippedProject> Skipped) result,
        CancellationToken ct)
    {
        Dictionary<string, Compilation> bareByAssembly = new(StringComparer.Ordinal);
        foreach (BareProject project in bare.Where(static p => p.Compilation is not null))
        {
            bareByAssembly.TryAdd(project.AssemblyName, project.Compilation!);
        }

        BareReferenceRebinder rebinder = new(bareByAssembly);
        foreach (Compilation compilation in sdkStyle)
        {
            Compilation rebound = rebinder.Rebind(compilation);
            if (ReferenceEquals(rebound, compilation))
            {
                result.Compilations.Add(compilation);
                continue;
            }

            // Its compiler errors were computed against MSBuildWorkspace's copy of the reference; they are recomputed.
            string name = nameByAssembly[compilation.AssemblyName!];
            result.Diagnostics.RemoveAll(d => string.Equals(d.Project, name, StringComparison.Ordinal) && d.Kind == LoadDiagnosticKind.CompilerError);
            ImmutableArray<LoadDiagnostic> errors = Errors(rebound, name, ct);
            if (errors.Any(static e => e.Kind == LoadDiagnosticKind.UnresolvedReference))
            {
                result.Skipped.Add(new SkippedProject(name, compilation.AssemblyName!, IsCSharp: true, errors, rebound));
            }
            else
            {
                result.Compilations.Add(rebound);
                result.Diagnostics.AddRange(errors);
            }
        }
    }

    /// <summary>
    /// The SDK-style projects through MSBuildWorkspace: the solution itself when every built project is SDK-style,
    /// else a temporary <c>.slnf</c> naming only them (P2-013's mechanism). When none of them loads, each becomes a
    /// skipped project, so the non-SDK projects can still carry the side.
    /// </summary>
    private async Task<LoadedSolution> LoadSdkStyleAsync(string solutionPath, ImmutableArray<string> sdkStyle, bool whole, CancellationToken ct)
    {
        string filterPath = Path.Combine(Path.GetTempPath(), $"equiv-{Guid.NewGuid():N}.slnf");
        try
        {
            if (!whole)
            {
                await File.WriteAllTextAsync(filterPath, SolutionBuildConfiguration.FilterJson(solutionPath, sdkStyle), ct).ConfigureAwait(false);
            }

            return await _sdkStyleLoader.LoadAsync(whole ? solutionPath : filterPath, ct).ConfigureAwait(false);
        }
        catch (SolutionLoadException exception)
        {
            return new LoadedSolution(
                EmptySolution(),
                [],
                [],
                [
                    .. exception.Diagnostics
                        .Where(static d => d.Kind != LoadDiagnosticKind.UnsupportedSolution)
                        .GroupBy(static d => d.Project, StringComparer.Ordinal)
                        .Select(static g => new SkippedProject(g.Key, g.Key, IsCSharp: !g.Any(static d => d.Kind == LoadDiagnosticKind.UnsupportedProject), [.. g], Compilation: null)),
                ]);
        }
        finally
        {
            File.Delete(filterPath);
        }
    }

    private static ImmutableArray<LoadDiagnostic> Errors(Compilation compilation, string project, CancellationToken ct) =>
    [
        .. compilation.GetDiagnostics(ct)
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new LoadDiagnostic(CompilationDiagnosticClassifier.Classify(d.Id), d.Id, project, d.GetMessage(CultureInfo.InvariantCulture))),
    ];

    /// <summary>Whether the project file is SDK-style; a missing or malformed one is left to the bare loader, which reports it.</summary>
    private static bool IsSdkStyle(string solutionDirectory, string projectPath) =>
        ProjectPath.Resolve(solutionDirectory, projectPath) is { } file && SdkStyleProject.IsSdkStyleFile(file);

    private static Solution EmptySolution()
    {
        using AdhocWorkspace workspace = new();
        return workspace.CurrentSolution;
    }
}
