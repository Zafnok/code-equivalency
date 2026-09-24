using System.Collections.Immutable;

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

        SolutionLoadException exception = await Assert.ThrowsAsync<SolutionLoadException>(() => loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken));

        Assert.True(created!.IsDisposed);
        LoadDiagnostic diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal((LoadDiagnosticKind.UnsupportedSolution, string.Empty, string.Empty), (diagnostic.Kind, diagnostic.Id, diagnostic.Project));
    }

    [Fact]
    public async Task Load_FailureEventNamingALoadedProjectSkipsIt()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource, filePath: @"C:\src\A\A.csproj");
            ws.AddCSharpProject("B", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\A\A.csproj' with message: evaluation failed");
        });

        Assert.Equal(["B"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("A", "A", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.NotNull(skipped.Compilation);
        LoadDiagnostic diagnostic = Assert.Single(skipped.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.WorkspaceFailure, diagnostic.Kind);
        Assert.Equal("A", diagnostic.Project);
    }

    [Fact]
    public async Task Load_FailureEventNamingNoProjectIsSkippedUnderTheEmptyName()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, "project file could not be evaluated");
        });

        Assert.Single(loaded.Compilations);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal((string.Empty, string.Empty, true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.Null(skipped.Compilation);
        Assert.Equal(new LoadDiagnostic(LoadDiagnosticKind.WorkspaceFailure, string.Empty, string.Empty, "project file could not be evaluated"), Assert.Single(skipped.Diagnostics));
    }

    [Fact]
    public async Task Load_FailureEventNamingAProjectThatNeverOpenedSkipsIt()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\Gone\Gone.csproj' with message: not found");
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\Gone\Gone.csproj' with message: still not found");
        });

        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("Gone", "Gone", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.Null(skipped.Compilation);
        Assert.Equal(2, skipped.Diagnostics.Length);
    }

    [Fact]
    public async Task Load_ProjectsThatNeverOpenedAreSkippedInNameOrder()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\Zed\Zed.csproj' with message: not found");
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\Alpha\Alpha.csproj' with message: not found");
        });

        Assert.Equal(["Alpha", "Zed"], loaded.Skipped.Select(static s => s.Name), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Load_AFailureIsMatchedToAProjectByItsFileNameNotItsName()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("Display", ValidSource, filePath: @"C:\src\File\File.csproj");
            ws.AddCSharpProject("B", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\File\File.csproj' with message: evaluation failed");
        });

        Assert.Equal(["B"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Assert.Equal("Display", Assert.Single(loaded.Skipped).Name);
    }

    [Fact]
    public async Task Load_AFailureIsMatchedToAProjectWithoutAFileByItsName()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.AddCSharpProject("B", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, @"Msbuild failed when processing the file 'C:\src\B\B.csproj' with message: evaluation failed");
        });

        Assert.Equal(["A"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal("B", skipped.Name);
        Assert.NotNull(skipped.Compilation);
    }

    [Theory]
    [InlineData("Native.vcxproj")]
    [InlineData("Setup.wixproj")]
    [InlineData("Db.sqlproj")]
    [InlineData("Legacy.vbproj")]
    [InlineData("Functional.fsproj")]
    public async Task ANonCSharpProjectIsSkippedWithAWarning(string file)
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource);
            ws.Raise(WorkspaceDiagnosticKind.Failure, $@"Cannot open project 'C:\src\{file}' because the file extension is not associated with a language.");
        });

        Assert.Single(loaded.Compilations);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.False(skipped.IsCSharp);
        Assert.Equal(Path.GetFileNameWithoutExtension(file), skipped.Name);
        Assert.Equal(LoadDiagnosticKind.UnsupportedProject, Assert.Single(skipped.Diagnostics).Kind);
    }

    [Fact]
    public async Task Load_FailureEventDuringCompilationSkipsThatProject()
    {
        LoadedSolution loaded = await LoadAsync(static ws =>
        {
            ws.AddCSharpProject("A", ValidSource, raiseOnTextLoad: new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, "document could not be read"));
            ws.AddCSharpProject("B", ValidSource);
        });

        Assert.Equal(["B"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal("A", skipped.Name);
        Assert.Contains(skipped.Diagnostics, static d => d is { Kind: LoadDiagnosticKind.WorkspaceFailure, Project: "A", Message: "document could not be read" });
    }

    [Fact]
    public async Task ProcessorArchitectureMismatchIsAWarning()
    {
        const string Message = @"Msbuild failed when processing the file 'C:\src\A\A.csproj' with message: warning MSB3270: There was a mismatch between the processor architecture";
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", ValidSource, filePath: @"C:\src\A\A.csproj");
            ws.Raise(WorkspaceDiagnosticKind.Failure, Message);
        });

        Assert.Single(loaded.Compilations);
        Assert.Empty(loaded.Skipped);
        Assert.Equal(new LoadDiagnostic(LoadDiagnosticKind.WorkspaceWarning, string.Empty, string.Empty, Message), Assert.Single(loaded.Diagnostics));
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
        Assert.Empty(loaded.Skipped);
    }

    [Fact]
    public async Task AProjectWithAnUnresolvedReferenceIsSkippedNotTheSolution()
    {
        LoadedSolution loaded = await LoadAsync(ws =>
        {
            ws.AddCSharpProject("A", "class C { Missing.Thing field; }");
            ws.AddCSharpProject("B", ValidSource);
        });

        Assert.Equal(["B"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("A", "A", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.NotNull(skipped.Compilation);
        LoadDiagnostic diagnostic = Assert.Single(skipped.Diagnostics, static d => string.Equals(d.Id, "CS0246", StringComparison.Ordinal));
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, diagnostic.Kind);
        Assert.Equal("A", diagnostic.Project);
        Assert.Contains("Missing", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroLoadableProjectsIsStillALoadFailure()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(ws =>
            ws.AddCSharpProject("A", "class C { Missing.Thing field; }")));

        Assert.Equal(LoadDiagnosticKind.UnsupportedSolution, ex.Diagnostics[0].Kind);
        Assert.Contains(ex.Diagnostics, static d => d is { Id: "CS0246", Kind: LoadDiagnosticKind.UnresolvedReference, Project: "A" });
    }

    [Fact]
    public async Task Load_MissingCoreLibraryIsAnUnresolvedReference()
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
        Assert.Empty(loaded.Skipped);
    }

    [Fact]
    public async Task Load_EmptySolutionIsRejected()
    {
        SolutionLoadException ex = await Assert.ThrowsAsync<SolutionLoadException>(() => LoadAsync(static _ => { }));

        LoadDiagnostic diagnostic = Assert.Single(ex.Diagnostics);
        Assert.Equal(LoadDiagnosticKind.UnsupportedSolution, diagnostic.Kind);
        Assert.Equal("the solution contains no C# project that loads", diagnostic.Message);
    }

    [Fact]
    public void NotCSharp_SkipsEachProjectWithItsFailures()
    {
        LoadDiagnostic failure = new(LoadDiagnosticKind.WorkspaceFailure, string.Empty, "Vb", "could not evaluate");
        Dictionary<string, (string Path, List<LoadDiagnostic> Failures)> failures = new(StringComparer.OrdinalIgnoreCase) { ["Vb"] = (@"C:\Vb.vbproj", [failure]) };

        ImmutableArray<SkippedProject> skipped = [.. MsBuildSolutionLoader.NotCSharp(
            [("Vb", "VbAssembly", LanguageNames.VisualBasic, "Vb"), ("Fs", "FsAssembly", LanguageNames.FSharp, "Fs")],
            failures)];

        Assert.Equal(["Vb", "Fs"], skipped.Select(static s => s.Name), StringComparer.Ordinal);
        Assert.All(skipped, static s => Assert.False(s.IsCSharp));
        Assert.Equal("VbAssembly", skipped[0].AssemblyName);
        Assert.Equal(
            [new LoadDiagnostic(LoadDiagnosticKind.UnsupportedProject, string.Empty, "Vb", "project language 'Visual Basic' is not supported; only C# is"), failure],
            skipped[0].Diagnostics);
        Assert.Single(skipped[1].Diagnostics);
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(@"C:\src\A\A.csproj", "A")]
    [InlineData("/src/A/A.csproj", "A")]
    [InlineData("A.csproj", "A")]
    [InlineData("", "")]
    public void Stem_SplitsOnEitherSeparator(string path, string stem) =>
        Assert.Equal(stem, MsBuildSolutionLoader.Stem(path));

    [Theory]
    [InlineData(@"Cannot open project 'C:\a b\N.vcxproj' because ...", @"C:\a b\N.vcxproj")]
    [InlineData("processing the file 'A.csproj' with message: 'x'", "A.csproj")]
    [InlineData("no project here", "")]
    public void ProjectPath_IsTheFirstQuotedProjectFile(string message, string path) =>
        Assert.Equal(path, MsBuildSolutionLoader.ProjectPath(message));

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

    [Fact]
    public async Task Load_OpensOnlyTheBuiltProjectsThroughATemporaryFilterAndNamesTheRest()
    {
        string? openedPath = null;
        string? filterJson = null;
        MsBuildSolutionLoader loader = new(
            () => new TestWorkspace(),
            async (ws, path, ct) =>
            {
                openedPath = path;
                filterJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                ((TestWorkspace)ws).AddCSharpProject("Lib", ValidSource);
                return ws.CurrentSolution;
            },
            _ => SolutionBuildConfigurationTests.Solution(built: ["Lib"], notBuilt: ["Site"]));

        LoadedSolution loaded = await loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken);

        Assert.EndsWith(".slnf", openedPath, StringComparison.Ordinal);
        Assert.False(File.Exists(openedPath));
        Assert.Contains("Lib.csproj", filterJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Site.csproj", filterJson, StringComparison.Ordinal);
        Assert.Equal(["Site"], loaded.NotBuilt);
        Assert.Empty(loaded.Skipped);
    }

    [Fact]
    public async Task Load_OpensASolutionWhoseProjectsAreAllBuiltAsItIs()
    {
        string? openedPath = null;
        MsBuildSolutionLoader loader = new(
            () => new TestWorkspace(),
            (ws, path, _) =>
            {
                openedPath = path;
                ((TestWorkspace)ws).AddCSharpProject("Lib", ValidSource);
                return Task.FromResult(ws.CurrentSolution);
            },
            _ => SolutionBuildConfigurationTests.Solution(built: ["Lib"], notBuilt: []));

        LoadedSolution loaded = await loader.LoadAsync(SolutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(SolutionPath, openedPath);
        Assert.Empty(loaded.NotBuilt);
    }

    [Fact]
    public void ReadSolution_ReadsAnExistingSlnOnly()
    {
        string directory = Path.Combine(Path.GetTempPath(), "equiv-P2-013-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string sln = Path.Combine(directory, "a.sln");
            string slnx = Path.Combine(directory, "a.slnx");
            File.WriteAllText(sln, "text");
            File.WriteAllText(slnx, "<Solution />");

            Assert.Equal("text", MsBuildSolutionLoader.ReadSolution(sln));
            Assert.Null(MsBuildSolutionLoader.ReadSolution(slnx));
            Assert.Null(MsBuildSolutionLoader.ReadSolution(Path.Combine(directory, "missing.sln")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
