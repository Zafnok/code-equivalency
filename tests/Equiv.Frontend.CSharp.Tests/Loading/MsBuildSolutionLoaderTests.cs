using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class MsBuildSolutionLoaderTests
{
    private const string SolutionPath = "side.sln";
    private const string ValidSource = "class C { int M() { int unused; return 1; } }";

    [Fact]
    public async Task Load_ValidSolutionReturnsOneCompilationPerProject()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.AddCSharpProject("B", ValidSource);
        });

        Assert.Equal(["A", "B"], loaded.Compilations.Select(static c => c.AssemblyName!).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(2, loaded.Solution.ProjectIds.Count);

        // The CS0168 "unused variable" warning is not an error and is not recorded.
        Assert.Empty(loaded.Diagnostics);
    }

    [Fact]
    public async Task Load_DisposesTheWorkspaceAfterCompilationsExist()
    {
        TestWorkspace? created = null;
        MsBuildSolutionLoader loader = new(
            () => created = new TestWorkspace(),
            (ws, _, _) =>
            {
                ((TestWorkspace)ws).AddCSharpProject("A", ValidSource);
                return Task.FromResult(ws.CurrentSolution);
            });

        LoadedSolution loaded = await loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken);

        Assert.True(created!.IsDisposed);

        // Compilations are immutable and outlive the workspace.
        Assert.Contains(loaded.Compilations[0].GetSymbolsWithName("M", SymbolFilter.Member, TestContext.Current.CancellationToken), static s => s.Kind == SymbolKind.Method);
    }

    [Fact]
    public async Task Load_DisposesTheWorkspaceWhenTheLoadAborts()
    {
        TestWorkspace? created = null;
        MsBuildSolutionLoader loader = new(
            () => created = new TestWorkspace(),
            (ws, _, _) => Task.FromResult(ws.CurrentSolution));

        await Assert.ThrowsAsync<SolutionLoadException>(() => loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken));

        Assert.True(created!.IsDisposed);
    }

    [Fact]
    public async Task Load_FailureEventAborts()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, "project file could not be evaluated");
        }));

        LoadDiagnostic diagnostic = Assert.Single(ex.Diagnostics);
        Assert.Equal(new LoadDiagnostic(LoadDiagnosticKind.WorkspaceFailure, string.Empty, string.Empty, "project file could not be evaluated"), diagnostic);
    }

    [Fact]
    public async Task Load_WarningEventIsKept()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Warning, "found project reference without a matching metadata reference");
        });

        LoadDiagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.WorkspaceWarning, diagnostic.Kind);
        Assert.Equal("found project reference without a matching metadata reference", diagnostic.Message);
        Assert.Single(loaded.Compilations);
    }

    [Fact]
    public async Task Load_UnresolvedReferenceAborts()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(ws =>
            ws.AddCSharpProject("A", "class C { Missing.Thing field; }")));

        LoadDiagnostic diagnostic = Assert.Single(ex.Diagnostics, static d => string.Equals(d.Id, "CS0246", StringComparison.Ordinal));
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, diagnostic.Kind);
        Assert.Equal("A", diagnostic.Project);
        Assert.Contains("Missing", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_MissingCoreLibraryAbortsAsUnresolvedReference()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(ws =>
            ws.AddCSharpProject("A", ValidSource, referenceCoreLibrary: false)));

        Assert.Contains(ex.Diagnostics, static d => d is { Id: "CS0518", Kind: LoadDiagnosticKind.UnresolvedReference });
    }

    [Fact]
    public async Task Load_OtherCompilerErrorIsKept()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
            ws.AddCSharpProject("A", "class C { int M() { int x = \"text\"; return x; } }"));

        LoadDiagnostic diagnostic = Assert.Single(loaded.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.CompilerError, diagnostic.Kind);
        Assert.Equal("CS0029", diagnostic.Id);
        Assert.Single(loaded.Compilations);
    }

    [Fact]
    public async Task Load_EmptySolutionIsRejected()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(static _ => { }));

        LoadDiagnostic diagnostic = Assert.Single(ex.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.UnsupportedSolution, diagnostic.Kind);
        Assert.Equal("the solution contains no C# project", diagnostic.Message);
    }

    [Fact]
    public void UnsupportedProjects_VisualBasicProjectIsRejected()
    {
        // Rejection is tested on (name, language) pairs: a real VB project needs the VB
        // workspace package, which this repo does not take a dependency on (ADR 0002).
        LoadDiagnostic diagnostic = Assert.Single(MsBuildSolutionLoader.UnsupportedProjects(
            [("Cs", LanguageNames.CSharp), ("Vb", LanguageNames.VisualBasic)]));

        Assert.Equal(
            new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, "Vb", "project language 'Visual Basic' is not supported; only C# is"),
            diagnostic);
    }

    [Fact]
    public void UnsupportedProjects_FSharpOnlySolutionIsRejected()
    {
        LoadDiagnostic diagnostic = Assert.Single(MsBuildSolutionLoader.UnsupportedProjects([("Fs", LanguageNames.FSharp)]));

        Assert.Equal("Fs", diagnostic.Project);
    }

    [Fact]
    public void UnsupportedProjects_AllCSharpIsAccepted()
    {
        Assert.Empty(MsBuildSolutionLoader.UnsupportedProjects([("A", LanguageNames.CSharp), ("B", LanguageNames.CSharp)]));
    }

    [Fact]
    public void SolutionLoadException_CarriesPathAndDiagnosticsInItsMessage()
    {
        SolutionLoadException ex = new(
            "x.sln",
            [new LoadDiagnostic(LoadDiagnosticKind.UnresolvedReference, "CS0246", "A", "type 'T' not found")]);

        Assert.Equal("x.sln", ex.Path);
        Assert.Single(ex.Diagnostics);
        Assert.Equal("failed to load 'x.sln': UnresolvedReference CS0246 A: type 'T' not found", ex.Message);
    }

    [Fact]
    public void DefaultConstructor_WiresTheMsBuildFactoryWithoutStartingIt()
    {
        ISolutionLoader loader = new MsBuildSolutionLoader();

        Assert.IsType<MsBuildSolutionLoader>(loader);
    }

    private static Task<LoadedSolution> LoadAsync(Action<TestWorkspace> open)
    {
        MsBuildSolutionLoader loader = new(
            static () => new TestWorkspace(),
            (ws, path, _) =>
            {
                Assert.Equal(SolutionPath, path);
                open((TestWorkspace)ws);
                return Task.FromResult(ws.CurrentSolution);
            });

        return loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken);
    }
}
