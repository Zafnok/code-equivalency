using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Loads a solution through MSBuildWorkspace (ADR 0004). Any workspace failure, unresolved
/// reference, or non-C# project aborts with <see cref="SolutionLoadException"/>.
/// </summary>
internal sealed class MsBuildSolutionLoader : ISolutionLoader
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
        ConcurrentQueue<LoadDiagnostic> workspaceEvents = new();

        // One workspace per side, disposed once the compilations exist; compilations are immutable and outlive it.
        using Workspace workspace = _createWorkspace();
        using IDisposable subscription = workspace.RegisterWorkspaceFailedHandler(e => workspaceEvents.Enqueue(ToLoadDiagnostic(e.Diagnostic)));

        Solution solution = await _openSolution(workspace, solutionPath, ct).ConfigureAwait(false);

        ImmutableArray<LoadDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<LoadDiagnostic>();
        diagnostics.AddRange(workspaceEvents);
        diagnostics.AddRange(UnsupportedProjects([.. solution.Projects.Select(static p => (p.Name, p.Language))]));
        ThrowIfAborting(solutionPath, diagnostics);

        ImmutableArray<Compilation>.Builder compilations = ImmutableArray.CreateBuilder<Compilation>();
        foreach (Project project in solution.Projects)
        {
            // Every project here is C# (checked above), so a compilation always exists.
            Compilation compilation = (await project.GetCompilationAsync(ct).ConfigureAwait(false))!;
            compilations.Add(compilation);
            diagnostics.AddRange(compilation.GetDiagnostics(ct)
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new LoadDiagnostic(CompilationDiagnosticClassifier.Classify(d.Id), d.Id, project.Name, d.GetMessage(CultureInfo.InvariantCulture))));
        }

        ThrowIfAborting(solutionPath, diagnostics);
        return new LoadedSolution(solution, compilations.ToImmutable(), diagnostics.ToImmutable());
    }

    /// <summary>Second line behind the router's extension check: only all-C# solutions load.</summary>
    internal static IEnumerable<LoadDiagnostic> UnsupportedProjects(IReadOnlyList<(string Name, string Language)> projects)
    {
        if (projects.Count == 0)
        {
            yield return new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, string.Empty, "the solution contains no C# project");
        }

        foreach ((string name, string language) in projects.Where(static p => !string.Equals(p.Language, LanguageNames.CSharp, StringComparison.Ordinal)))
        {
            yield return new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, name, $"project language '{language}' is not supported; only C# is");
        }
    }

    private static LoadDiagnostic ToLoadDiagnostic(WorkspaceDiagnostic diagnostic) => new(
        diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? LoadDiagnosticKind.WorkspaceFailure : LoadDiagnosticKind.WorkspaceWarning,
        string.Empty,
        string.Empty,
        diagnostic.Message);

    private static void ThrowIfAborting(string solutionPath, ImmutableArray<LoadDiagnostic>.Builder diagnostics)
    {
        if (diagnostics.Any(static d => d.Kind is LoadDiagnosticKind.WorkspaceFailure or LoadDiagnosticKind.UnresolvedReference or LoadDiagnosticKind.UnsupportedSolution))
        {
            throw new SolutionLoadException(solutionPath, diagnostics.ToImmutable());
        }
    }
}
