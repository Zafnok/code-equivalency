using System.Text.Json;

using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

/// <summary>M3-029: non-SDK projects through the bare loader, driven through <see cref="CompositeSolutionLoader"/> as off Windows.</summary>
public sealed class BareProjectLoaderTests : IDisposable
{
    private const string Code = "namespace App { public class Calculator { public int Add(int a, int b) { return a + b; } } }";

    private readonly BareFixture _fixture = new();

    public BareProjectLoaderTests() => _fixture.Framework();

    [Fact]
    public async Task ANonSdkProjectLoadsWithItsSourcesReferencesOptionsAndTheGeneratedFrameworkAttribute()
    {
        string project = Legacy("App", """
              <PropertyGroup><AssemblyName>App.Core</AssemblyName><DefineConstants>DEBUG;TRACE</DefineConstants></PropertyGroup>
              <ItemGroup><Reference Include="System" /><Compile Include="Code.cs" /></ItemGroup>
            """);

        LoadedSolution loaded = await Load(Solution(project));

        Compilation compilation = Assert.Single(loaded.Compilations);
        Assert.Equal("App.Core", compilation.AssemblyName);
        Assert.Empty(loaded.Skipped);
        Assert.Empty(loaded.Diagnostics);
        Assert.Empty(loaded.NotBuilt);
        string directory = Path.GetDirectoryName(project)!;
        string attributes = Path.Combine(directory, "obj", "Debug", ".NETFramework,Version=v4.8.AssemblyAttributes.cs");
        Assert.Equal([Path.Combine(directory, "Code.cs"), attributes], compilation.SyntaxTrees.Select(static t => t.FilePath), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "TargetFrameworkAttribute(\".NETFramework,Version=v4.8\", FrameworkDisplayName = \".NET Framework 4.8\")",
            compilation.SyntaxTrees.Last().ToString(),
            StringComparison.Ordinal);
        Assert.Equal(["mscorlib.dll", "System.dll", "System.Core.dll"], compilation.References.Select(static r => Path.GetFileName(((PortableExecutableReference)r).FilePath)), StringComparer.Ordinal);
        Assert.Equal(["DEBUG", "TRACE"], compilation.SyntaxTrees.First().Options.PreprocessorSymbolNames, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TheFrameworkAttributeFileIsReadFromDiskWhenABuildLeftOne()
    {
        string project = Legacy("App", """<PropertyGroup><IntermediateOutputPath>out\</IntermediateOutputPath></PropertyGroup><ItemGroup><Compile Include="Code.cs" /></ItemGroup>""");
        string onDisk = _fixture.Write(Path.Combine(Path.GetDirectoryName(project)!, "out", ".NETFramework,Version=v4.8.AssemblyAttributes.cs"), "// from disk\n");

        Compilation compilation = Assert.Single((await Load(Solution(project))).Compilations);

        Assert.Equal(onDisk, compilation.SyntaxTrees.Last().FilePath, ignoreCase: true);
        Assert.Equal("// from disk\n", compilation.SyntaxTrees.Last().ToString());
    }

    [Theory]
    [InlineData("<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>", "v4.8", false)]
    [InlineData("", "v3.5", false)]
    [InlineData("<GenerateTargetFrameworkAttribute>true</GenerateTargetFrameworkAttribute>", "v3.5", true)]
    public async Task TheFrameworkAttributeIsGeneratedFromDotNet4OrWhenAsked(string property, string frameworkVersion, bool generated)
    {
        _fixture.Framework(frameworkVersion);
        string project = Legacy("App", $"""<PropertyGroup>{property}</PropertyGroup><ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", frameworkVersion: frameworkVersion);

        Compilation compilation = Assert.Single((await Load(Solution(project))).Compilations);

        Assert.Equal(generated ? 2 : 1, compilation.SyntaxTrees.Count());
    }

    [Fact]
    public async Task AProfileSelectsItsFolderAndMoniker()
    {
        string framework = _fixture.Framework("v4.0");
        foreach (string file in (string[])["mscorlib.dll", "System.Core.dll"])
        {
            _fixture.WriteBytes(Path.Combine(framework, "Profile", "Client", file), await File.ReadAllBytesAsync(Path.Combine(framework, file), TestContext.Current.CancellationToken));
        }

        string project = Legacy(
            "App",
            """<PropertyGroup><TargetFrameworkProfile>Client</TargetFrameworkProfile><BaseIntermediateOutputPath>build\</BaseIntermediateOutputPath></PropertyGroup><ItemGroup><Compile Include="Code.cs" /></ItemGroup>""",
            frameworkVersion: "v4.0");

        Compilation compilation = Assert.Single((await Load(Solution(project))).Compilations);

        Assert.All(compilation.References, r => Assert.Contains(Path.Combine("Profile", "Client"), ((PortableExecutableReference)r).FilePath, StringComparison.Ordinal));
        Assert.EndsWith(Path.Combine("build", "Debug", ".NETFramework,Version=v4.0,Profile=Client.AssemblyAttributes.cs"), compilation.SyntaxTrees.Last().FilePath, StringComparison.Ordinal);
        Assert.Contains("FrameworkDisplayName = \"\"", compilation.SyntaxTrees.Last().ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageReferencesTakeTheirAssetsFromTheRestoredAssetsFile()
    {
        string asset = _fixture.Library(Path.Combine("nuget", "web", "1.0.0", "lib", "net45", "Web.dll"), "Web", "namespace Web { public class Controller { } }");
        string project = Legacy("App", """
              <ItemGroup><PackageReference Include="Web" Version="1.0.0" /><Compile Include="Code.cs" /></ItemGroup>
            """, code: "public class Home : Web.Controller { }");
        WriteAssets(Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json"), "Web/1.0.0", "web/1.0.0", "lib/net45/Web.dll", ["System.Numerics"]);

        Compilation compilation = Assert.Single((await Load(Solution(project))).Compilations);

        Assert.Contains(compilation.References, r => string.Equals(((PortableExecutableReference)r).FilePath, asset, StringComparison.Ordinal));
        Assert.Contains(compilation.References, static r => string.Equals(Path.GetFileName(((PortableExecutableReference)r).FilePath), "System.Numerics.dll", StringComparison.Ordinal));
    }

    /// <summary>
    /// As in MSBuild, <c>BaseIntermediateOutputPath</c> moves the assets file only when it is set before
    /// <c>Microsoft.Common.props</c> computes the extensions path, or when the project does not import that file.
    /// </summary>
    [Theory]
    [InlineData("<ProjectAssetsFile>custom\\assets.json</ProjectAssetsFile>", true, "custom/assets.json")]
    [InlineData("<MSBuildProjectExtensionsPath>ext\\</MSBuildProjectExtensionsPath>", true, "ext/project.assets.json")]
    [InlineData("<BaseIntermediateOutputPath>late\\</BaseIntermediateOutputPath>", true, "obj/project.assets.json")]
    [InlineData("<BaseIntermediateOutputPath>intermediate\\</BaseIntermediateOutputPath>", false, "intermediate/project.assets.json")]
    [InlineData("", false, "obj/project.assets.json")]
    public async Task TheAssetsFileLocationFollowsTheProject(string property, bool importsCommonProps, string location)
    {
        _fixture.Library(Path.Combine("nuget", "web", "1.0.0", "lib", "net45", "Web.dll"), "Web", "namespace Web { public class Controller { } }");
        string project = Legacy("App", $"""<PropertyGroup>{property}</PropertyGroup><ItemGroup><PackageReference Include="Web" /><Compile Include="Code.cs" /></ItemGroup>""", code: "public class Home : Web.Controller { }");
        if (!importsCommonProps)
        {
            string[] lines = await File.ReadAllLinesAsync(project, TestContext.Current.CancellationToken);
            await File.WriteAllLinesAsync(project, lines.Where(static l => !l.Contains("Microsoft.Common.props", StringComparison.Ordinal)), TestContext.Current.CancellationToken);
        }

        WriteAssets(Path.Combine(Path.GetDirectoryName(project)!, location), "Web/1.0.0", "web/1.0.0", "lib/net45/Web.dll", []);

        Assert.Single((await Load(Solution(project))).Compilations);
    }

    [Fact]
    public async Task ACompilerErrorIsKeptAndAnUnresolvedReferenceSkipsTheProject()
    {
        string kept = Legacy("Kept", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", code: "public class Kept { public int M() { return \"x\"; } }");
        string unresolved = Legacy("Unresolved", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", code: "public class Unresolved : Missing.Type { }");

        LoadedSolution loaded = await Load(Solution(kept, unresolved));

        Assert.Equal("Kept", Assert.Single(loaded.Compilations).AssemblyName);
        LoadDiagnostic error = Assert.Single(loaded.Diagnostics);
        Assert.Equal((LoadDiagnosticKind.CompilerError, "CS0029", "Kept"), (error.Kind, error.Id, error.Project));
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("Unresolved", "Unresolved", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.NotNull(skipped.Compilation);
        Assert.Contains(skipped.Diagnostics, static d => d is { Kind: LoadDiagnosticKind.UnresolvedReference, Id: "CS0246" });
    }

    public static TheoryData<string, string> Constructs => new()
    {
        { """<Choose><When Condition="true"><PropertyGroup><X>1</X></PropertyGroup></When></Choose>""", "<Choose>" },
        { """<PropertyGroup><AssemblyName>$([System.String]::Concat('A', 'B'))</AssemblyName></PropertyGroup>""", "the property function $([System.String]::Concat('A', 'B'))" },
        { """<ItemGroup><Compile Include="Extra.cs" Condition="'$(Configuration)' ~= 'Debug'" /></ItemGroup>""", "a condition outside the supported grammar" },
        { """<Target Name="Generate" BeforeTargets="CoreCompile"><ItemGroup><Compile Include="obj\Generated.cs" /></ItemGroup></Target>""", "a <Target> that creates Compile items (target 'Generate')" },
        { """<ItemGroup><COMReference Include="Microsoft.Office.Interop.Word" /></ItemGroup>""", "<COMReference>" },
        { """<Import Project="..\build\Missing.targets" />""", @"the missing import '..\build\Missing.targets'" },
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public async Task UnsupportedConstructSkipsTheProject(string body, string construct)
    {
        string good = Legacy("Good", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""");
        string bad = Legacy("Bad", body + """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""");

        LoadedSolution loaded = await Load(Solution(good, bad));

        Assert.Equal("Good", Assert.Single(loaded.Compilations).AssemblyName);
        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal(("Bad", true), (skipped.Name, skipped.IsCSharp));
        Assert.Null(skipped.Compilation);
        LoadDiagnostic failure = Assert.Single(skipped.Diagnostics);
        Assert.Equal((LoadDiagnosticKind.WorkspaceFailure, "Bad"), (failure.Kind, failure.Project));
        Assert.StartsWith($"the bare loader skipped '{bad}': the bare loader cannot evaluate {construct}", failure.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Failures => new()
    {
        { """<PropertyGroup><TargetFrameworkIdentifier>Silverlight</TargetFrameworkIdentifier></PropertyGroup>""", "the bare loader cannot evaluate the target framework 'Silverlight'" },
        { """<PropertyGroup><TargetFrameworkVersion>4.8</TargetFrameworkVersion></PropertyGroup>""", "the bare loader cannot evaluate the TargetFrameworkVersion '4.8'" },
        { """<PropertyGroup><TargetFrameworkVersion>v9.9</TargetFrameworkVersion></PropertyGroup>""", "no reference assemblies for .NETFramework,Version=v9.9 are cached in" },
        { """<ItemGroup><PackageReference Include="Web" /></ItemGroup>""", "it has PackageReference items but no" },
        { """<ItemGroup><Compile Include="Missing.cs" /></ItemGroup>""", "the source file" },
        { """<Import Project="Broken.props" />""", "a project file it imports is not well-formed XML" },
        { """<ItemGroup><ProjectReference Include="..\Modern\Modern.csproj" /></ItemGroup>""", "the bare loader cannot evaluate the reference to the SDK-style project" },
        { """<PropertyGroup><ProjectAssetsFile>bad.json</ProjectAssetsFile></PropertyGroup><ItemGroup><PackageReference Include="Web" /></ItemGroup>""", "its project.assets.json cannot be read" },
        { """<PropertyGroup><ProjectAssetsFile>empty.json</ProjectAssetsFile></PropertyGroup><ItemGroup><PackageReference Include="Web" /></ItemGroup>""", "its project.assets.json cannot be read" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AProjectThatCannotBeLoadedExactlyIsSkippedAndSaysWhy(string body, string reason)
    {
        string good = Legacy("Good", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""");
        string bad = Legacy("Bad", body);
        _fixture.Write(Path.Combine(Path.GetDirectoryName(bad)!, "Broken.props"), "<Project>");
        _fixture.Write(Path.Combine(Path.GetDirectoryName(bad)!, "bad.json"), "{");
        _fixture.Write(Path.Combine(Path.GetDirectoryName(bad)!, "empty.json"), """{ "targets": {}, "libraries": {}, "packageFolders": {} }""");
        _fixture.Write(Path.Combine(Path.GetDirectoryName(good)!, "..", "Modern", "Modern.csproj"), """<Project Sdk="Microsoft.NET.Sdk" />""");

        LoadedSolution loaded = await Load(Solution(good, bad));

        SkippedProject skipped = Assert.Single(loaded.Skipped);
        Assert.Equal("Bad", skipped.Name);
        Assert.StartsWith($"the bare loader skipped '{bad}': {reason}", Assert.Single(skipped.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingOrMalformedProjectFileOrAnotherLanguageIsSkipped()
    {
        string good = Legacy("Good", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""");
        string malformed = _fixture.Write(Path.Combine("sln", "Malformed", "Malformed.csproj"), "<Project>");
        string basic = _fixture.Write(Path.Combine("sln", "Basic", "Basic.vbproj"), BareFixture.Project(string.Empty));
        string solution = _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution(Relative(good), @"Gone\Gone.csproj", Relative(malformed), Relative(basic)));

        LoadedSolution loaded = await Load(solution);

        string gone = Path.Combine(_fixture.Root, "sln", "Gone", "Gone.csproj");
        Assert.Equal(
            [("Gone", true, LoadDiagnosticKind.WorkspaceFailure), ("Malformed", true, LoadDiagnosticKind.WorkspaceFailure), ("Basic", false, LoadDiagnosticKind.UnsupportedProject)],
            loaded.Skipped.Select(static s => (s.Name, s.IsCSharp, Assert.Single(s.Diagnostics).Kind)));
        Assert.Equal($"the bare loader skipped '{gone}': the project file '{gone}' does not exist", loaded.Skipped[0].Diagnostics[0].Message);
        Assert.StartsWith($"the bare loader skipped '{malformed}': a project file it imports is not well-formed XML: ", loaded.Skipped[1].Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Equal("project type '.vbproj' is not supported; only C# is", loaded.Skipped[2].Diagnostics[0].Message);
    }

    [Fact]
    public async Task AReferencedNonSdkProjectLoadsOnceEvenFromOutsideTheSolution()
    {
        Legacy("Library", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", code: "namespace Lib { public class Helper { } }");
        string first = Legacy("First", """<ItemGroup><ProjectReference Include="..\Library\Library.csproj" Aliases="lib" /><Compile Include="Code.cs" /></ItemGroup>""", code: "extern alias lib; public class First : lib::Lib.Helper { }");
        string second = Legacy("Second", """<ItemGroup><ProjectReference Include="..\LIBRARY\library.csproj" /><ProjectReference Include="..\Gone\Gone.csproj" /><ProjectReference Include="..\First\First.csproj"><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference><Compile Include="Code.cs" /></ItemGroup>""", code: "public class Second : Lib.Helper { }");

        LoadedSolution loaded = await Load(Solution(first, second));

        Assert.Equal(["Library", "First", "Second"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Compilation helper = loaded.Compilations[0];
        Assert.Equal(["lib"], loaded.Compilations[1].References.OfType<CompilationReference>().Single().Properties.Aliases, StringComparer.Ordinal);
        Assert.Same(helper, loaded.Compilations[2].References.OfType<CompilationReference>().Single().Compilation);
        Assert.Equal("Gone", Assert.Single(loaded.Skipped).Name);
    }

    [Fact]
    public async Task AProjectReferenceCycleSkipsTheProjectThatClosesIt()
    {
        string a = Legacy("A", """<ItemGroup><ProjectReference Include="..\B\B.csproj" /><Compile Include="Code.cs" /></ItemGroup>""", code: "public class A { }");
        Legacy("B", """<ItemGroup><ProjectReference Include="..\A\A.csproj" /><Compile Include="Code.cs" /></ItemGroup>""", code: "public class B { }");

        LoadedSolution loaded = await Load(Solution(a));

        Assert.Equal(["B", "A"], loaded.Compilations.Select(static c => c.AssemblyName!), StringComparer.Ordinal);
        Assert.Empty(loaded.Compilations[0].References.OfType<CompilationReference>());
        Assert.Empty(loaded.Skipped);
    }

    [Fact]
    public async Task ASideWithNoProjectThatLoadsFails()
    {
        string bad = Legacy("Bad", """<ItemGroup><Compile Include="Missing.cs" /></ItemGroup>""");

        SolutionLoadException exception = await Assert.ThrowsAsync<SolutionLoadException>(() => Load(Solution(bad)));

        Assert.Equal([LoadDiagnosticKind.UnsupportedSolution, LoadDiagnosticKind.WorkspaceFailure], exception.Diagnostics.Select(static d => d.Kind));
    }

    [Fact]
    public async Task PackagesConfigPackagesAreRestoredBeforeTheProjectLoads()
    {
        string project = Legacy("App", """
              <ItemGroup>
                <Reference Include="Web"><HintPath>..\packages\Web.1.0.0\lib\net45\Web.dll</HintPath></Reference>
                <Compile Include="Code.cs" />
              </ItemGroup>
            """, code: "public class Home : Web.Controller { }");
        _fixture.Write(Path.Combine(Path.GetDirectoryName(project)!, "packages.config"), """<packages><package id="Web" version="1.0.0" /><package id="Absent" version="1.0.0" /></packages>""");
        byte[] web = BareFixture.Emit("Web", "namespace Web { public class Controller { } }", BareFixture.CoreLibraryReference);
        _fixture.WriteBytes(Path.Combine("feed", "Web.1.0.0.nupkg"), BareFixture.Package(("lib/net45/Web.dll", web)));
        _fixture.Write(Path.Combine("sln", "nuget.config"), """<configuration><packageSources><clear /><add key="local" value="..\feed" /></packageSources></configuration>""");

        LoadedSolution loaded = await Load(Solution(project));

        Assert.Equal("App", Assert.Single(loaded.Compilations).AssemblyName);
        Assert.Equal("Absent", Assert.Single(loaded.Diagnostics).Message.Split('\'')[1]);
    }

    [Fact]
    public async Task ReferenceAssembliesAreFetchedFromTheSolutionsSources()
    {
        _fixture.WriteBytes(Path.Combine("feed", "Microsoft.NETFramework.ReferenceAssemblies.net472.1.0.3.nupkg"), BareFixture.ReferenceAssemblyPackage("v4.7.2"));
        _fixture.Write(Path.Combine("sln", "nuget.config"), """<configuration><packageSources><clear /><add key="local" value="..\feed" /></packageSources></configuration>""");
        string project = Legacy("App", """<ItemGroup><Compile Include="Code.cs" /></ItemGroup>""", frameworkVersion: "v4.7.2");

        Compilation compilation = Assert.Single((await Load(Solution(project))).Compilations);

        Assert.Equal(Path.Combine(_fixture.ReferenceAssemblies, ".NETFramework", "v4.7.2", "mscorlib.dll"), ((PortableExecutableReference)compilation.References.First()).FilePath);
    }

    public void Dispose() => _fixture.Dispose();

    private string Legacy(string name, string body, string code = Code, string frameworkVersion = "v4.8")
    {
        _fixture.Write(Path.Combine("sln", name, "Code.cs"), code);
        return _fixture.Write(Path.Combine("sln", name, name + ".csproj"), BareFixture.Project(body, frameworkVersion));
    }

    private string Relative(string project) => Path.GetRelativePath(Path.Combine(_fixture.Root, "sln"), project).Replace('/', '\\');

    private string Solution(params string[] projects) =>
        _fixture.Write(Path.Combine("sln", "side.sln"), BareFixture.Solution([.. projects.Select(Relative)]));

    private Task<LoadedSolution> Load(string solution) => _fixture.Loader().LoadAsync(solution, TestContext.Current.CancellationToken);

    private void WriteAssets(string path, string library, string libraryPath, string asset, string[] frameworkAssemblies) =>
        _fixture.Write(path, JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["targets"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [".NETFramework,Version=v4.8"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [library] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["type"] = "package",
                        ["compile"] = new Dictionary<string, object>(StringComparer.Ordinal) { [asset] = new { } },
                        ["frameworkAssemblies"] = frameworkAssemblies,
                    },
                },
            },
            ["libraries"] = new Dictionary<string, object>(StringComparer.Ordinal) { [library] = new { path = libraryPath } },
            ["packageFolders"] = new Dictionary<string, object>(StringComparer.Ordinal) { [Path.Combine(_fixture.Root, "nuget")] = new { } },
        }));
}
