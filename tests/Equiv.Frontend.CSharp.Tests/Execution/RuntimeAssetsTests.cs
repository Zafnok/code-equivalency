using System.Collections.Immutable;
using System.Text.Json.Nodes;

using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-240, over a fake restore: a package whose compile asset is a reference assembly is deployed from the runtime
/// asset its <c>project.assets.json</c> chose for the machine's runtime identifier, a targeting pack is still not copied,
/// and a package whose runtime asset cannot be found is the error, naming it. The package is shaped as
/// <c>System.Management</c> 4.7.0 is: <c>ref/netstandard2.0</c> to compile, a <c>lib/netstandard2.0</c> that runs
/// anywhere, and <c>runtimes/win/lib/netcoreapp2.0</c> for Windows.
/// </summary>
public sealed class RuntimeAssetsTests : IDisposable
{
    private const string RefAsset = "ref/netstandard2.0/Pkg.dll";
    private const string LibAsset = "lib/netstandard2.0/Pkg.dll";
    private const string WinAsset = "runtimes/win/lib/netcoreapp2.0/Pkg.dll";

    private static readonly IReadOnlyList<string> Windows = RuntimeAssets.Chain("win-x64");

    private readonly string root = Directory.CreateTempSubdirectory("runtime-assets-").FullName;

    public RuntimeAssetsTests()
    {
        Directory.CreateDirectory(Packages);
        Directory.CreateDirectory(Empty);
    }

    private string Packages => Path.Combine(root, "packages");

    /// <summary>A package folder with nothing in it, listed first: NuGet looks for each asset in every folder, in order.</summary>
    private string Empty => Path.Combine(root, "fallback");

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Theory]
    [InlineData("win-x64", new[] { "win-x64", "win", "any" })]
    [InlineData("linux-x64", new[] { "linux-x64", "linux", "unix-x64", "unix", "any" })]
    [InlineData("osx-arm64", new[] { "osx-arm64", "osx", "unix-arm64", "unix", "any" })]
    [InlineData("win", new[] { "win", "any" })]
    [InlineData("any", new[] { "any" })]
    public void Chain_IsThePortableFallbackGraph(string rid, string[] chain) => Assert.Equal(chain, RuntimeAssets.Chain(rid));

    [Fact]
    public void AReferenceAssemblyPackage_IsDeployedFromItsWindowsRuntimeAsset()
    {
        PkgLayout();
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());
        string output = Path.Combine(root, "out");

        string? error = ProjectEmitter.Emit(project, output, new DriverKey(), Assets(("Project", Restore(PkgTarget())))).Error;

        Assert.Null(error);
        Assert.Equal(File.ReadAllBytes(Asset("pkg/1.0.0", WinAsset)), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
        Assert.True(File.Exists(Path.Combine(output, "Project.dll")));
    }

    [Fact]
    public void OnARuntimeIdentifierNoRuntimeTargetNames_ThePlainRuntimeAssetIsDeployed()
    {
        PkgLayout();
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());
        string output = Path.Combine(root, "out");

        string? error = ProjectEmitter.Emit(project, output, new DriverKey(), Assets(("Project", Restore(PkgTarget())), RuntimeAssets.Chain("linux-x64"))).Error;

        Assert.Null(error);
        Assert.Equal(File.ReadAllBytes(Asset("pkg/1.0.0", LibAsset)), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
    }

    [Fact]
    public void AMissingRuntimeAsset_IsTheErrorNamingThePackage()
    {
        PkgLayout();
        File.Delete(Asset("pkg/1.0.0", WinAsset));
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());

        string? error = ProjectEmitter.Emit(project, Path.Combine(root, "out"), new DriverKey(), Assets(("Project", Restore(PkgTarget())))).Error;

        Assert.Equal($"Project: the package Pkg 1.0.0 runs {Path.Combine(Empty, "pkg", "1.0.0", "runtimes", "win", "lib", "netcoreapp2.0", "Pkg.dll")}, which is not on disk", error);
        Assert.False(File.Exists(Path.Combine(root, "out", "Project.dll")));
    }

    [Fact]
    public void AReferenceAssemblyPackageNoRestoreNames_IsTheError()
    {
        PkgLayout();
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());

        string? error = ProjectEmitter.Emit(project, Path.Combine(root, "out"), new DriverKey(), RuntimeAssets.None).Error;

        Assert.Equal("Project: the package pkg 1.0.0 is compiled against its reference assembly, and no restore names its runtime asset", error);
    }

    /// <summary>A targeting pack restored as a package has a package's shape, and lists its assemblies in <c>data/FrameworkList.xml</c>.</summary>
    [Fact]
    public void ATargetingPackRestoredAsAPackage_IsNotCopied()
    {
        string pack = Path.Combine(Packages, "microsoft.netcore.app.ref", "10.0.0");
        Library(Path.Combine(pack, "ref", "net10.0", "Ref.dll"), "Ref", "[assembly: System.Runtime.CompilerServices.ReferenceAssembly] public class R { }");
        File.WriteAllText(Path.Combine(pack, "microsoft.netcore.app.ref.nuspec"), "<package />");
        Directory.CreateDirectory(Path.Combine(pack, "data"));
        File.WriteAllText(Path.Combine(pack, "data", "FrameworkList.xml"), "<FileList />");
        CSharpCompilation project = Compile("public class C { R r; }", "Project", MetadataReference.CreateFromFile(Path.Combine(pack, "ref", "net10.0", "Ref.dll")));
        string output = Path.Combine(root, "out");

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), Assets(("Project", Restore(PkgTarget())))).Error);
        Assert.False(File.Exists(Path.Combine(output, "Ref.dll")));
    }

    /// <summary>A package whose runtime asset is NuGet's empty placeholder runs what the runtime supplies, as the build output does.</summary>
    [Fact]
    public void APackageThatRunsNothing_DeploysNothing()
    {
        PkgLayout();
        JsonObject target = new()
        {
            ["Pkg/1.0.0"] = Entry(compile: [RefAsset], runtimeTargets: new JsonObject { ["runtimes/win/lib/net45/_._"] = Rid("runtime", "win") }),
        };
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());
        string output = Path.Combine(root, "out");

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), Assets(("Project", Restore(target)))).Error);
        Assert.False(File.Exists(Path.Combine(output, "Pkg.dll")));
    }

    /// <summary>A <c>packages.config</c> project references the package's <c>lib/&lt;tfm&gt;</c> folder, which is its implementation.</summary>
    [Fact]
    public void APackagesConfigReference_IsCopiedAsItIs()
    {
        string lib = Path.Combine(root, "solution", "packages", "Pkg.1.0.0", "lib", "net48", "Pkg.dll");
        Library(lib, "Pkg", "public class P { }");
        CSharpCompilation project = Compile("public class C { P p; }", "Project", MetadataReference.CreateFromFile(lib));
        string output = Path.Combine(root, "out");

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), RuntimeAssets.None).Error);
        Assert.Equal(File.ReadAllBytes(lib), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
    }

    /// <summary>
    /// A referenced project compiles against version 1.0.0, and the deployed project's restore unified it to 2.0.0, which
    /// is what its build output runs.
    /// </summary>
    [Fact]
    public void AReferencedProjectsPackage_IsResolvedByTheDeployedProjectsRestore()
    {
        PkgLayout();
        PkgLayout("2.0.0");
        CSharpCompilation dependency = Compile("public class D { P p; }", "Dependency", Ref());
        CSharpCompilation project = Compile("public class C { D d; }", "Project", dependency.ToMetadataReference(), Ref("2.0.0"));
        string output = Path.Combine(root, "out");
        RuntimeAssets assets = Assets(("Project", Restore(PkgTarget("2.0.0"))), ("Dependency", Restore(PkgTarget())));

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), assets).Error);
        Assert.Equal(File.ReadAllBytes(Asset("pkg/2.0.0", WinAsset)), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APackageOnlyAReferencedProjectRestores_IsResolvedByItsOwnRestore(bool deployedHasRestore)
    {
        PkgLayout();
        CSharpCompilation dependency = Compile("public class D { P p; }", "Dependency", Ref());
        CSharpCompilation project = Compile("public class C { D d; }", "Project", dependency.ToMetadataReference());
        string output = Path.Combine(root, "out");
        RuntimeAssets assets = deployedHasRestore
            ? Assets(("Project", Restore([])), ("Dependency", Restore(PkgTarget())))
            : Assets(("Dependency", Restore(PkgTarget())));

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), assets).Error);
        Assert.Equal(File.ReadAllBytes(Asset("pkg/1.0.0", WinAsset)), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
    }

    /// <summary>
    /// A project that targets two frameworks has a target for each, and one per runtime identifier: the compilation's own
    /// is the target without one whose compile assets it references.
    /// </summary>
    [Fact]
    public void TheTargetIsTheOneTheCompilationReferences()
    {
        PkgLayout();
        JsonObject net48 = new() { ["Pkg/1.0.0"] = Entry(compile: ["lib/net48/Pkg.dll"], runtime: ["lib/net48/Pkg.dll"]) };
        JsonObject rid = new() { ["Pkg/1.0.0"] = Entry(compile: [RefAsset], runtime: ["lib/net48/Pkg.dll"]) };
        string file = Restore(net48, PkgTarget(), rid);
        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());
        string output = Path.Combine(root, "out");

        Assert.Null(ProjectEmitter.Emit(project, output, new DriverKey(), Assets(("Project", file))).Error);
        Assert.Equal(File.ReadAllBytes(Asset("pkg/1.0.0", WinAsset)), File.ReadAllBytes(Path.Combine(output, "Pkg.dll")));
    }

    /// <summary>A restore that cannot be read, has only per-runtime-identifier targets, or is gone names no package: a reference assembly is then the error.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("""{ "packageFolders": [] }""")]
    [InlineData("""{ "packageFolders": {}, "libraries": {}, "targets": { "net10.0/win-x64": {} } }""")]
    [InlineData(null)]
    public void ARestoreThatNamesNothing_LeavesAReferenceAssemblyUnrestored(string? json)
    {
        PkgLayout();
        string file = Path.Combine(root, "obj", "project.assets.json");
        if (json is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, json);
        }

        CSharpCompilation project = Compile("public class C { P p; }", "Project", Ref());

        string? error = ProjectEmitter.Emit(project, Path.Combine(root, "out"), new DriverKey(), Assets(("Project", file))).Error;

        Assert.EndsWith("is compiled against its reference assembly, and no restore names its runtime asset", error, StringComparison.Ordinal);
    }

    /// <summary>A reference assembly outside a package's <c>ref/&lt;tfm&gt;</c> folder is a pack, whatever folder it is in.</summary>
    [Theory]
    [InlineData("X.dll")]
    [InlineData("a/X.dll")]
    [InlineData("a/b/X.dll")]
    [InlineData("a/b/c/X.dll")]
    public void AReferenceAssemblyOutsideAPackage_IsNotOne(string relative)
    {
        string path = Path.Combine(Path.GetPathRoot(root)!, "equiv-p2-240-absent", relative);
        CSharpCompilation project = Compile(string.Empty, "Project");

        Assert.Null(RuntimeAssets.None.For(project, project, path, referenceAssembly: true));
        Assert.Null(RuntimeAssets.None.For(project, project, Path.Combine(Path.GetPathRoot(root)!, "X.dll"), referenceAssembly: true));
    }

    [Fact]
    public void AReferenceAssemblyInARefFolderWithoutANuspec_IsNotAPackage()
    {
        string path = Path.Combine(Packages, "loose", "1.0.0", "ref", "net10.0", "Loose.dll");
        Library(path, "Loose", "public class L { }");
        CSharpCompilation project = Compile(string.Empty, "Project");

        Assert.Null(RuntimeAssets.None.For(project, project, path, referenceAssembly: true));
    }

    [Fact]
    public void Of_FindsEachProjectsRestoreAboveItsIntermediateAssemblyOrInItsObjFolder()
    {
        string intermediate = Path.Combine(root, "A", "artifacts", "obj", "Debug", "net10.0", "win-x64", "A.dll");
        string aRestore = Write(Path.Combine(root, "A", "artifacts", "obj", "project.assets.json"));
        string bRestore = Write(Path.Combine(root, "B", "obj", "project.assets.json"));
        Write(Path.Combine(root, "D", "obj", "project.assets.json"));
        using AdhocWorkspace workspace = new();
        Solution solution = workspace.CurrentSolution;
        solution = Add(solution, "A", Path.Combine(root, "A", "A.csproj"), intermediate);
        solution = Add(solution, "B", Path.Combine(root, "B", "B.csproj"), Path.Combine(root, "B", "bin", "B.dll"));
        solution = Add(solution, "B", Path.Combine(root, "D", "D.csproj"), assembly: null);
        solution = Add(solution, "C", filePath: null, Path.Combine(Path.GetPathRoot(root)!, "C.dll"));

        RuntimeAssets assets = RuntimeAssets.Of(solution, "win-x64");

        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["A"] = aRestore, ["B"] = bRestore }, assets.RestoreFiles);
    }

    private static Solution Add(Solution solution, string assemblyName, string? filePath, string? assembly)
    {
        ProjectId id = ProjectId.CreateNewId();
        return solution
            .AddProject(ProjectInfo.Create(id, VersionStamp.Create(), assemblyName, assemblyName, LanguageNames.CSharp, filePath: filePath))
            .WithProjectCompilationOutputInfo(id, default(CompilationOutputInfo).WithAssemblyPath(assembly));
    }

    private static string Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        return path;
    }

    /// <summary>A <c>Pkg</c> package at <paramref name="version"/>: a reference assembly, a plain runtime asset and a Windows one, each a different file.</summary>
    private void PkgLayout(string version = "1.0.0")
    {
        string package = Path.Combine(Packages, "pkg", version);
        Library(Path.Combine(package, RefAsset), "Pkg", "[assembly: System.Runtime.CompilerServices.ReferenceAssembly] public class P { public static string Where() => throw null; }");
        Library(Path.Combine(package, LibAsset), "Pkg", "public class P { public static string Where() => \"anywhere\"; }");
        Library(Path.Combine(package, WinAsset), "Pkg", "public class P { public static string Where() => \"win\"; }");
        File.WriteAllText(Path.Combine(package, "pkg.nuspec"), "<package />");
    }

    private string Asset(string library, string asset) => Path.GetFullPath(Path.Combine(Packages, library, asset));

    private PortableExecutableReference Ref(string version = "1.0.0") => MetadataReference.CreateFromFile(Asset($"pkg/{version}", RefAsset));

    /// <summary>The target entry for <c>Pkg</c> as NuGet writes <c>System.Management</c> 4.7.0's, beside entries no package is deployed from.</summary>
    private static JsonObject PkgTarget(string version = "1.0.0") => new()
    {
        [$"Pkg/{version}"] = Entry(
            compile: [RefAsset],
            runtime: [LibAsset],
            runtimeTargets: new JsonObject
            {
                [WinAsset] = Rid("runtime", "win"),
                ["runtimes/win-x64/native/pkg.native.dll"] = Rid("native", "win-x64"),
            }),
        ["Other/1.0.0"] = new JsonObject { ["type"] = "project" },
        ["Untyped/1.0.0"] = new JsonObject(),
        ["RuntimeOnly/1.0.0"] = Entry(runtime: ["lib/netstandard2.0/RuntimeOnly.dll"]),
    };

    private static JsonObject Entry(string[]? compile = null, string[]? runtime = null, JsonObject? runtimeTargets = null)
    {
        JsonObject entry = new() { ["type"] = "package" };
        if (compile is not null)
        {
            entry["compile"] = Assets(compile);
        }

        if (runtime is not null)
        {
            entry["runtime"] = Assets(runtime);
        }

        if (runtimeTargets is not null)
        {
            entry["runtimeTargets"] = runtimeTargets;
        }

        return entry;
    }

    private static JsonObject Assets(string[] assets)
    {
        JsonObject result = [];
        foreach (string asset in assets)
        {
            result[asset] = new JsonObject();
        }

        return result;
    }

    private static JsonObject Rid(string assetType, string rid) => new() { ["assetType"] = assetType, ["rid"] = rid };

    /// <summary>Writes a <c>project.assets.json</c> with <paramref name="targets"/> as <c>net10.0</c>, <c>net48</c>... in order, and returns its path.</summary>
    private string Restore(params JsonObject[] targets)
    {
        string[] names = targets.Length == 3 ? [".NETFramework,Version=v4.8", "net10.0", "net10.0/win-x64"] : ["net10.0"];
        JsonObject json = new()
        {
            ["version"] = 3,
            ["targets"] = new JsonObject(targets.Select((t, i) => KeyValuePair.Create(names[i], (JsonNode?)t))),
            ["libraries"] = new JsonObject
            {
                ["Pkg/1.0.0"] = new JsonObject { ["path"] = "pkg/1.0.0" },
                ["Pkg/2.0.0"] = new JsonObject { ["path"] = "pkg/2.0.0" },
                ["RuntimeOnly/1.0.0"] = new JsonObject { ["path"] = "runtimeonly/1.0.0" },
            },
            ["packageFolders"] = new JsonObject { [Empty + Path.DirectorySeparatorChar] = new JsonObject(), [Packages + Path.DirectorySeparatorChar] = new JsonObject() },
        };
        string path = Path.Combine(root, "obj", Path.GetRandomFileName(), "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString());
        return path;
    }

    private static RuntimeAssets Assets(params (string Project, string File)[] restores) => Assets(restores, Windows);

    private static RuntimeAssets Assets((string Project, string File)[] restores, IReadOnlyList<string> rids) =>
        new(restores.ToImmutableDictionary(static r => r.Project, static r => r.File, StringComparer.Ordinal), rids);

    private static RuntimeAssets Assets((string Project, string File) restore, IReadOnlyList<string> rids) => Assets([restore], rids);

    private static void Library(string path, string name, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using MemoryStream image = new();
        EmitResult result = Compile(source, name).Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join('\n', result.Diagnostics));
        File.WriteAllBytes(path, image.ToArray());
    }
}
