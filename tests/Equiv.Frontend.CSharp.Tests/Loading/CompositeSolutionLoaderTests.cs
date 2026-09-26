using System.Collections.Immutable;
using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

/// <summary>M3-029: the loader off Windows, with MSBuildWorkspace replaced by an in-memory workspace.</summary>
public sealed class CompositeSolutionLoaderTests : IDisposable
{
    private const string SdkProject = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""";

    private readonly BareFixture _fixture = new();

    public CompositeSolutionLoaderTests() => _fixture.Framework();

    [Fact]
    public async Task ASolutionOfSdkStyleProjectsIsOpenedWholeByMsBuildWorkspace()
    {
        string modern = _fixture.Write(Path.Combine("sln", "Modern", "Modern.csproj"), SdkProject);
        string solution = _fixture.Write(Path.Combine("sln", "side.slnx"), """<Solution><Project Path="Modern\Modern.csproj" /></Solution>""");
        List<string> opened = [];

        LoadedSolution loaded = await _fixture.Loader(SdkLoader(opened, (ws, _) => ws.AddCSharpProject("Modern", "public class M { }", filePath: modern)))
            .LoadAsync(solution, TestContext.Current.CancellationToken);

        Assert.Equal([solution], opened, StringComparer.Ordinal);
        Assert.Equal("Modern", Assert.Single(loaded.Compilations).AssemblyName);
        Assert.Equal("Modern", Assert.Single(loaded.Solution.Projects).Name);
    }

    [Fact]
    public async Task ANonSdkProjectReferencingAnSdkProjectBindsAgainstItsCompilation()
    {
        string modern = _fixture.Write(Path.Combine("sln", "Modern", "Modern.csproj"), SdkProject);
        string legacy = Legacy("Legacy", """<ItemGroup><ProjectReference Include="..\Modern\Modern.csproj" /><Compile Include="Code.cs" /></ItemGroup>""", "public class L : M { }");
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), SolutionBuildConfigurationTests.Solution(built: ["Modern", "Legacy"], notBuilt: ["Site"]));
        List<string> opened = [];
        List<string> filtered = [];

        LoadedSolution loaded = await _fixture.Loader(SdkLoader(opened, (ws, path) =>
            {
                filtered.AddRange(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray().Select(static p => p.GetString()!));
                AddProject(ws, "Modern", modern, "public class M { }", []);
            }))
            .LoadAsync(solution, TestContext.Current.CancellationToken);

        Assert.Equal([modern], filtered, StringComparer.Ordinal);
        Assert.EndsWith(".slnf", Assert.Single(opened), StringComparison.Ordinal);
        Assert.False(File.Exists(opened[0]));
        Assert.Equal(["Modern", "Legacy"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Assert.Same(loaded.Compilations[0], loaded.Compilations[1].References.OfType<CompilationReference>().Single().Compilation);
        Assert.Equal(["Site"], loaded.NotBuilt, StringComparer.Ordinal);
        _ = legacy;
    }

    [Fact]
    public async Task ANonSdkProjectBindsAgainstASkippedSdkProjectsCompilationToo()
    {
        string modern = _fixture.Write(Path.Combine("sln", "Modern", "Modern.csproj"), SdkProject);
        Legacy("Legacy", """<ItemGroup><ProjectReference Include="..\Modern\Modern.csproj" /><Compile Include="Code.cs" /></ItemGroup>""", "public class L : M { }");
        string other = _fixture.Write(Path.Combine("sln", "Other", "Other.csproj"), SdkProject);
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution(@"Modern\Modern.csproj", @"Other\Other.csproj", @"Legacy\Legacy.csproj"));

        LoadedSolution loaded = await _fixture.Loader(SdkLoader([], (ws, _) =>
            {
                AddProject(ws, "Modern", modern, "public class M { public Missing.Type Value; }", []);
                AddProject(ws, "Other", other, "public class O { }", []);
            }))
            .LoadAsync(solution, TestContext.Current.CancellationToken);

        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal("Modern", skipped.Name);
        Assert.Equal(["Other", "Legacy"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Assert.Same(skipped.Compilation, loaded.Compilations[1].References.OfType<CompilationReference>().Single().Compilation);
    }

    [Fact]
    public async Task ASdkProjectReferencingALegacyProjectBindsAgainstTheBareCompilation()
    {
        (string modern, string legacy) = ModernReferencingLegacy(bareHelper: "public class Helper { public static int Value() { return 1; } }");

        LoadedSolution loaded = await LoadWithMsBuildCopy(modern, legacy, copyHelper: "public class Helper { public static string Value() { return null; } }");

        Assert.Equal(["Modern", "Legacy"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Compilation bare = loaded.Compilations[1];
        Assert.Same(bare, loaded.Compilations[0].References.OfType<CompilationReference>().Single().Compilation);
        Assert.Contains(Path.Combine("sln", "Legacy", "Code.cs"), bare.SyntaxTrees.First().FilePath, StringComparison.Ordinal);

        // MSBuildWorkspace's copy made Modern's return a CS0029; bound against the bare compilation it compiles clean.
        Assert.Empty(loaded.Diagnostics);
        Assert.Empty(loaded.Skipped);
    }

    [Fact]
    public async Task ASdkProjectWhoseBareReferenceNoLongerResolvesIsSkipped()
    {
        (string modern, string legacy) = ModernReferencingLegacy(bareHelper: "public class Other { }");

        LoadedSolution loaded = await LoadWithMsBuildCopy(modern, legacy, copyHelper: "public class Helper { public static int Value() { return 1; } }");

        Assert.Equal(["Legacy"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("Modern", "Modern", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.Contains(skipped.Diagnostics, static d => d.Kind == LoadDiagnosticKind.UnresolvedReference);
        Assert.Same(loaded.Compilations[0], skipped.Compilation!.References.OfType<CompilationReference>().Single().Compilation);
    }

    [Fact]
    public async Task ASdkProjectThatGainsACompilerErrorFromTheBareReferenceKeepsLoadingWithIt()
    {
        (string modern, string legacy) = ModernReferencingLegacy(bareHelper: "public class Helper { public static string Value() { return null; } }");

        LoadedSolution loaded = await LoadWithMsBuildCopy(modern, legacy, copyHelper: "public class Helper { public static int Value() { return 1; } }");

        Assert.Equal(["Modern", "Legacy"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        LoadDiagnostic error = Assert.Single(loaded.Diagnostics);
        Assert.Equal((LoadDiagnosticKind.CompilerError, "CS0029", "Modern"), (error.Kind, error.Id, error.Project));
    }

    [Fact]
    public async Task ACompilationThatReachesABareProjectThroughAnotherIsRebuiltToo()
    {
        string outer = _fixture.Write(Path.Combine("sln", "Outer", "Outer.csproj"), SdkProject);
        string inner = _fixture.Write(Path.Combine("sln", "Inner", "Inner.csproj"), SdkProject);
        string legacy = Legacy("Legacy", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", "public class Helper { }");
        string unrelated = _fixture.Library(Path.Combine("lib", "Unrelated.dll"), "Unrelated", "public class U { }");
        string builtLegacy = _fixture.WriteBytes(Path.Combine("sln", "Legacy", "bin", "Legacy.dll"), BareFixture.Emit("Legacy", "public class Helper { }", BareFixture.CoreLibraryReference));
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution(@"Outer\Outer.csproj", @"Inner\Inner.csproj", @"Legacy\Legacy.csproj"));

        LoadedSolution loaded = await _fixture.Loader(SdkLoader([], (ws, _) =>
            {
                ProjectId innerId = AddProject(ws, "Inner", inner, "public class I { public Helper H; }", [MetadataReference.CreateFromFile(builtLegacy)]);
                AddProject(ws, "Outer", outer, "public class O { public I Value; }", [MetadataReference.CreateFromFile(unrelated)], innerId);
            }))
            .LoadAsync(solution, TestContext.Current.CancellationToken);

        Compilation bare = loaded.Compilations.Single(static c => c.AssemblyName is "Legacy");
        Compilation rebuiltInner = loaded.Compilations.Single(static c => c.AssemblyName is "Inner");
        Compilation rebuiltOuter = loaded.Compilations.Single(static c => c.AssemblyName is "Outer");
        Assert.Same(bare, rebuiltInner.References.OfType<CompilationReference>().Single().Compilation);
        Assert.Same(rebuiltInner, rebuiltOuter.References.OfType<CompilationReference>().Single().Compilation);
        Assert.Contains(rebuiltOuter.References, static r => r is PortableExecutableReference { FilePath: { } path } && path.EndsWith("Unrelated.dll", StringComparison.Ordinal));
        Assert.Empty(loaded.Skipped);
        _ = legacy;
    }

    [Fact]
    public async Task WhenNoSdkStyleProjectLoadsTheNonSdkOnesStillCarryTheSide()
    {
        _fixture.Write(Path.Combine("sln", "Modern", "Modern.csproj"), SdkProject);
        Legacy("Legacy", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", "public class L { }");
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution(@"Modern\Modern.csproj", @"Legacy\Legacy.csproj", @"Native\Native.vcxproj"));
        FailingLoader sdk = new(
            [
                new LoadDiagnostic(LoadDiagnosticKind.UnsupportedSolution, string.Empty, string.Empty, "no C# project loads"),
                new LoadDiagnostic(LoadDiagnosticKind.WorkspaceFailure, string.Empty, "Modern", "the build host failed"),
                new LoadDiagnostic(LoadDiagnosticKind.UnresolvedReference, "CS0246", "Modern", "missing type"),
                new LoadDiagnostic(LoadDiagnosticKind.UnsupportedProject, string.Empty, "Tool", "not C#"),
            ]);

        LoadedSolution loaded = await _fixture.Loader(sdk).LoadAsync(solution, TestContext.Current.CancellationToken);

        Assert.Equal("Legacy", Assert.Single(loaded.Compilations).AssemblyName);
        Assert.Equal(
            [("Modern", true, 2), ("Tool", false, 1), ("Native", false, 1)],
            loaded.Skipped.Select(static s => (s.Name, s.IsCSharp, s.Diagnostics.Length)));
        Assert.Empty(loaded.Solution.Projects);
    }

    public void Dispose() => _fixture.Dispose();

    private static ProjectId AddProject(TestWorkspace workspace, string name, string path, string source, ImmutableArray<MetadataReference> references, params ProjectId[] projectReferences)
    {
        ProjectId id = ProjectId.CreateNewId(name);
        workspace.AddProject(ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: path,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            documents: [DocumentInfo.Create(DocumentId.CreateNewId(id), name + ".cs", loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())))],
            projectReferences: [.. projectReferences.Select(static p => new ProjectReference(p))],
            metadataReferences: [BareFixture.CoreLibraryReference, .. references]));
        return id;
    }

    private static MsBuildSolutionLoader SdkLoader(List<string> opened, Action<TestWorkspace, string> open) => new(
        static () => new TestWorkspace(),
        (ws, path, _) =>
        {
            opened.Add(path);
            open((TestWorkspace)ws, path);
            return Task.FromResult(ws.CurrentSolution);
        });

    /// <summary>An SDK-style <c>Modern</c> that uses <c>Helper.Value()</c> from a non-SDK <c>Legacy</c> it references; only Modern is in the solution.</summary>
    private (string Modern, string Legacy) ModernReferencingLegacy(string bareHelper)
    {
        string modern = _fixture.Write(Path.Combine("sln", "Modern", "Modern.csproj"), SdkProject);
        string legacy = Legacy("Legacy", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", bareHelper);
        return (modern, legacy);
    }

    /// <summary>Loads a solution of <paramref name="modern"/> alone, whose MSBuildWorkspace copy of <paramref name="legacy"/> holds <paramref name="copyHelper"/>.</summary>
    private Task<LoadedSolution> LoadWithMsBuildCopy(string modern, string legacy, string copyHelper)
    {
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution(@"Modern\Modern.csproj"));
        return _fixture.Loader(SdkLoader([], (ws, _) =>
            {
                ProjectId copy = AddProject(ws, "Legacy", legacy, copyHelper, []);
                AddProject(ws, "Modern", modern, "public class M { public Helper Instance; public int Get() { return Helper.Value(); } }", [], copy);
            }))
            .LoadAsync(solution, TestContext.Current.CancellationToken);
    }

    private string Legacy(string name, string body, string code)
    {
        _fixture.Write(Path.Combine("sln", name, "Code.cs"), code);
        return _fixture.Write(Path.Combine("sln", name, name + ".csproj"), BareFixture.Project(body));
    }

    private sealed class FailingLoader(ImmutableArray<LoadDiagnostic> diagnostics) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) => throw new SolutionLoadException(solutionPath, diagnostics);
    }
}
