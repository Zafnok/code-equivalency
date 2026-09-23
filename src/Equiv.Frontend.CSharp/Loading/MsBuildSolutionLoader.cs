using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Loads a solution through MSBuildWorkspace (ADR 0004), containing each fault to its project (ADR 0029 decision 1).
/// A C# project with a workspace failure or an unresolved reference is skipped, and so is a project in another
/// language. Only a side with no C# project left aborts, with <see cref="SolutionLoadException"/>.
/// </summary>
internal sealed partial class MsBuildSolutionLoader : ISolutionLoader
{
    private readonly Func<Workspace> _createWorkspace;
    private readonly Func<Workspace, string, CancellationToken, Task<Solution>> _openSolution;

    public MsBuildSolutionLoader()
        : this(MsBuildWorkspaceFactory.Create, MsBuildWorkspaceFactory.OpenSolutionAsync)
    {
    }

    /// <summary>Seam for unit tests: the same load path over an <c>AdhocWorkspace</c>.</summary>
    internal MsBuildSolutionLoader(
        Func<Workspace> createWorkspace,
        Func<Workspace, string, CancellationToken, Task<Solution>> openSolution)
    {
        _createWorkspace = createWorkspace;
        _openSolution = openSolution;
    }

    public async Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct)
    {
        // WorkspaceFailed is raised on a background thread.
        ConcurrentQueue<WorkspaceDiagnostic> workspaceEvents = new();

        // One workspace per side, disposed once the compilations exist; compilations are immutable and outlive it.
        using Workspace workspace = _createWorkspace();
        using IDisposable subscription = workspace.RegisterWorkspaceFailedHandler(e => workspaceEvents.Enqueue(e.Diagnostic));

        Solution solution = await _openSolution(workspace, solutionPath, ct).ConfigureAwait(false);

        // Opening events name their project in the message, if at all; "" collects the ones that name none.
        List<LoadDiagnostic> kept = [];
        Dictionary<string, (string Path, List<LoadDiagnostic> Failures)> failuresByProject = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkspaceDiagnostic diagnostic in Drain(workspaceEvents))
        {
            Attribute(ToLoadDiagnostic(diagnostic, fallbackProject: string.Empty), kept, failuresByProject);
        }

        ImmutableArray<Compilation>.Builder compilations = ImmutableArray.CreateBuilder<Compilation>();
        ImmutableArray<SkippedProject>.Builder skipped = ImmutableArray.CreateBuilder<SkippedProject>();
        skipped.AddRange(NotCSharp(
            [.. solution.Projects.Where(static p => !IsCSharp(p)).Select(static p => (p.Name, p.AssemblyName, p.Language, FileStem(p)))],
            failuresByProject));
        foreach (Project project in solution.Projects.Where(IsCSharp))
        {
            string key = FileStem(project);
            (Compilation compilation, ImmutableArray<LoadDiagnostic> diagnostics) = await CompileAsync(
                project,
                failuresByProject.Remove(key, out (string Path, List<LoadDiagnostic> Failures) opening) ? opening.Failures : [],
                workspaceEvents,
                ct).ConfigureAwait(false);
            if (diagnostics.Any(static d => d.Kind is LoadDiagnosticKind.WorkspaceFailure or LoadDiagnosticKind.UnresolvedReference))
            {
                skipped.Add(new SkippedProject(project.Name, project.AssemblyName, IsCSharp: true, diagnostics, compilation));
            }
            else
            {
                compilations.Add(compilation);
                kept.AddRange(diagnostics);
            }
        }

        skipped.AddRange(NeverOpened(failuresByProject));

        if (compilations.Count == 0)
        {
            throw new SolutionLoadException(
                solutionPath,
                [new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, string.Empty, "the solution contains no C# project that loads"), .. skipped.SelectMany(static s => s.Diagnostics)]);
        }

        return new LoadedSolution(solution, compilations.ToImmutable(), [.. kept], skipped.ToImmutable());
    }

    /// <summary>
    /// Failures naming a project the workspace never added: it did not open at all, so its name stands in for its
    /// assembly name. Failures naming no project are collected under the empty name.
    /// </summary>
    private static IEnumerable<SkippedProject> NeverOpened(Dictionary<string, (string Path, List<LoadDiagnostic> Failures)> failuresByProject) =>
        failuresByProject
            .OrderBy(static p => p.Key, StringComparer.Ordinal)
            .Select(static p => new SkippedProject(
                p.Key,
                p.Key,
                IsCSharp: p.Value.Path.Length == 0 || p.Value.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase),
                [.. p.Value.Failures],
                Compilation: null));

    /// <summary>
    /// A C# project's compilation plus every diagnostic that bears on it: its compiler errors, the events raised while
    /// compiling it (e.g. a document that fails to load), and the <paramref name="openingFailures"/> that named it.
    /// </summary>
    private static async Task<(Compilation Compilation, ImmutableArray<LoadDiagnostic> Diagnostics)> CompileAsync(
        Project project,
        List<LoadDiagnostic> openingFailures,
        ConcurrentQueue<WorkspaceDiagnostic> workspaceEvents,
        CancellationToken ct)
    {
        // A C# project always has a compilation.
        Compilation compilation = (await project.GetCompilationAsync(ct).ConfigureAwait(false))!;
        ImmutableArray<LoadDiagnostic> diagnostics =
        [
            .. compilation.GetDiagnostics(ct)
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new LoadDiagnostic(CompilationDiagnosticClassifier.Classify(d.Id), d.Id, project.Name, d.GetMessage(CultureInfo.InvariantCulture))),
            .. Drain(workspaceEvents).Select(d => ToLoadDiagnostic(d, project.Name)),
            .. openingFailures,
        ];
        return (compilation, diagnostics);
    }

    /// <summary>
    /// Files a workspace diagnostic under the project its message names. A failure naming a project file that is
    /// not <c>.csproj</c> (<c>.vcxproj</c>, <c>.vbproj</c>, ...) becomes <see cref="LoadDiagnosticKind.UnsupportedProject"/>.
    /// </summary>
    private static void Attribute(LoadDiagnostic diagnostic, List<LoadDiagnostic> kept, Dictionary<string, (string Path, List<LoadDiagnostic> Failures)> failuresByProject)
    {
        if (diagnostic.Kind != LoadDiagnosticKind.WorkspaceFailure)
        {
            kept.Add(diagnostic);
            return;
        }

        string path = ProjectPath(diagnostic.Message);
        string name = Path.GetFileNameWithoutExtension(path);
        LoadDiagnostic attributed = path.Length == 0 || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? diagnostic with { Project = name }
            : diagnostic with { Kind = LoadDiagnosticKind.UnsupportedProject, Project = name };
        if (!failuresByProject.TryGetValue(name, out (string Path, List<LoadDiagnostic> Failures) entry))
        {
            entry = (path, []);
            failuresByProject[name] = entry;
        }

        entry.Failures.Add(attributed);
    }

    /// <summary>The first quoted <c>*.??proj</c> path in a workspace message, or empty.</summary>
    internal static string ProjectPath(string message) => ProjectFile.Match(message) is { Success: true } match ? match.Groups["path"].Value : string.Empty;

    private static string FileStem(Project project) => Path.GetFileNameWithoutExtension(project.FilePath) is { Length: > 0 } stem ? stem : project.Name;

    private static bool IsCSharp(Project project) => string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal);

    /// <summary>
    /// Every project the workspace opened in another language, skipped with a warning (ADR 0029), together with the
    /// failures that named it. Tested on tuples: a real VB project needs the VB workspace package, which this repo
    /// does not take a dependency on (ADR 0002).
    /// </summary>
    internal static IEnumerable<SkippedProject> NotCSharp(
        IReadOnlyList<(string Name, string AssemblyName, string Language, string Key)> projects,
        Dictionary<string, (string Path, List<LoadDiagnostic> Failures)> failuresByProject)
    {
        foreach ((string name, string assemblyName, string language, string key) in projects)
        {
            ImmutableArray<LoadDiagnostic> failures = failuresByProject.Remove(key, out (string Path, List<LoadDiagnostic> Failures) entry) ? [.. entry.Failures] : [];
            LoadDiagnostic unsupported = new(LoadDiagnosticKind.UnsupportedProject, string.Empty, name, $"project language '{language}' is not supported; only C# is");
            yield return new SkippedProject(name, assemblyName, IsCSharp: false, [unsupported, .. failures], Compilation: null);
        }
    }

    private static IEnumerable<WorkspaceDiagnostic> Drain(ConcurrentQueue<WorkspaceDiagnostic> events)
    {
        while (events.TryDequeue(out WorkspaceDiagnostic? diagnostic))
        {
            yield return diagnostic;
        }
    }

    private static LoadDiagnostic ToLoadDiagnostic(WorkspaceDiagnostic diagnostic, string fallbackProject) => new(
        diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(diagnostic.Message) : LoadDiagnosticKind.WorkspaceWarning,
        string.Empty,
        fallbackProject,
        diagnostic.Message);

    [GeneratedRegex(@"'(?<path>[^']+\.[A-Za-z]*proj)'", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectFile { get; }
}
