using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class AssemblyReferencesTests : IDisposable
{
    private static readonly ProjectAssets NoAssets = new([], []);

    private readonly BareFixture _fixture = new();

    [Fact]
    public void HintPathsResolveWithBackslashesAndWrongCase()
    {
        _fixture.Framework();
        string thing = _fixture.Library(Path.Combine("packages", "Thing.1.0.0", "lib", "net45", "Thing.dll"), "Thing", "public class Thing { }");

        ImmutableArray<ResolvedReference> references = Resolve("""
              <ItemGroup>
                <Reference Include="Thing, Version=1.0.0.0, Culture=neutral, processorArchitecture=MSIL">
                  <HintPath>..\PACKAGES\thing.1.0.0\LIB\net45\thing.dll</HintPath>
                </Reference>
              </ItemGroup>
            """);

        Assert.Equal(thing, references.Single(static r => Path.GetFileName(r.Path).Equals("thing.dll", StringComparison.OrdinalIgnoreCase)).Path, ignoreCase: true);
    }

    [Fact]
    public void AReferenceResolvesByHintPathThenFrameworkThenFacadesThenFileName()
    {
        string framework = _fixture.Framework();
        _fixture.Library(Path.Combine("p", "local", "Local.dll"), "Local", "public class Local { }");

        ImmutableArray<ResolvedReference> references = Resolve("""
              <ItemGroup>
                <Reference Include="System"><HintPath>missing\System.dll</HintPath></Reference>
                <Reference Include="System.Runtime" />
                <Reference Include="local\Local.dll" />
                <Reference Include="Nowhere" />
                <Reference Include="Aliased" Aliases="one, two"><HintPath>local\Local.dll</HintPath><EmbedInteropTypes>True</EmbedInteropTypes></Reference>
              </ItemGroup>
            """);

        Assert.Equal(
            [
                ("mscorlib.dll", framework, "", false),
                ("System.dll", framework, "", false),
                ("System.Runtime.dll", Path.Combine(framework, "Facades"), "", false),
                ("Local.dll", Path.Combine(_fixture.Root, "p", "local"), "", false),
                ("System.Core.dll", framework, "", false),
                ("netstandard.dll", Path.Combine(framework, "Facades"), "", false),
            ],
            references.Select(static r => (Path.GetFileName(r.Path), Path.GetDirectoryName(r.Path)!, string.Join(',', r.Aliases), r.EmbedInteropTypes)));
    }

    [Fact]
    public void TheFirstReferenceWithAFileNameKeepsItsAliases()
    {
        _fixture.Framework();
        _fixture.Library(Path.Combine("p", "local", "Local.dll"), "Local", "public class Local { }");

        ImmutableArray<ResolvedReference> references = Resolve("""
              <ItemGroup>
                <Reference Include="Aliased" Aliases="one, two"><HintPath>local\Local.dll</HintPath><EmbedInteropTypes>True</EmbedInteropTypes></Reference>
                <Reference Include="local\Local.dll" />
              </ItemGroup>
            """);

        ResolvedReference local = references.Single(static r => r.Path.EndsWith("Local.dll", StringComparison.Ordinal));
        Assert.Equal(["one", "two"], local.Aliases);
        Assert.True(local.EmbedInteropTypes);
    }

    [Theory]
    [InlineData("v4.8", "", "mscorlib.dll|System.Core.dll")]
    [InlineData("v3.5", "", "mscorlib.dll|System.Core.dll")]
    [InlineData("v2.0", "", "mscorlib.dll")]
    [InlineData("v4.8", "<NoStdLib>true</NoStdLib>", "System.Core.dll")]
    [InlineData("v4.8", "<AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences>", "mscorlib.dll")]
    public void ImplicitReferencesAndFacades(string frameworkVersion, string property, string expected)
    {
        _fixture.Framework(frameworkVersion);

        ImmutableArray<ResolvedReference> references = Resolve($"<PropertyGroup>{property}</PropertyGroup>", frameworkVersion);

        Assert.Equal(expected, Names(references));
    }

    [Theory]
    [InlineData("v4.8", "System.Runtime", "", "Uses.dll|System.Runtime.dll|netstandard.dll")]
    [InlineData("v4.8", "System.Runtime", "<ImplicitlyExpandDesignTimeFacades>false</ImplicitlyExpandDesignTimeFacades>", "Uses.dll")]
    [InlineData("v4.7.2", "netstandard", "", "Uses.dll|netstandard.dll")]
    [InlineData("v4.7.2", "netstandard", "<ImplicitlyExpandNETStandardFacades>false</ImplicitlyExpandNETStandardFacades>", "Uses.dll")]
    [InlineData("v4.6.1", "netstandard", "", "Uses.dll|System.Shim.dll|netstandard.dll")]
    [InlineData("v4.7.1", "netstandard", "", "Uses.dll|netstandard.dll")]
    [InlineData("v4.6", "netstandard", "", "Uses.dll")]
    public void AReferenceThatDependsOnSystemRuntimeOrNetStandardPullsInFacades(string frameworkVersion, string dependency, string property, string expected)
    {
        string framework = _fixture.Framework(frameworkVersion);
        string shims = Path.Combine(_fixture.Root, "shims");
        _fixture.Library(Path.Combine("shims", "netstandard.dll"), "netstandard", "public class StandardType { }");
        _fixture.Library(Path.Combine("shims", "System.Shim.dll"), "System.Shim", "public class Shim { }");
        _fixture.Write(Path.Combine("shims", "readme.txt"), "not an assembly");
        _fixture.Write(Path.Combine(framework, "Facades", "readme.txt"), "not an assembly");
        string facade = Path.Combine(framework, "Facades", dependency + ".dll");
        string type = string.Equals(dependency, "netstandard", StringComparison.Ordinal) ? "StandardType" : "RuntimeType";
        _fixture.Library(Path.Combine("p", "lib", "Uses.dll"), "Uses", $"public class Uses : {type} {{ }}", facade);

        ImmutableArray<ResolvedReference> references = Resolve(
            $"""<PropertyGroup>{property}<NoStdLib>true</NoStdLib><AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences></PropertyGroup><ItemGroup><Reference Include="Uses"><HintPath>lib\Uses.dll</HintPath></Reference></ItemGroup>""",
            frameworkVersion,
            shims: shims);

        Assert.Equal(expected, Names(references));
    }

    [Fact]
    public void WithoutTheSdksShimsANetStandardDependencyBefore471AddsNothing()
    {
        string framework = _fixture.Framework("v4.6.1");
        _fixture.Library(Path.Combine("p", "lib", "Uses.dll"), "Uses", "public class Uses : StandardType { }", Path.Combine(framework, "Facades", "netstandard.dll"));

        ImmutableArray<ResolvedReference> references = Resolve(
            """<PropertyGroup><NoStdLib>true</NoStdLib><AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences></PropertyGroup><ItemGroup><Reference Include="lib\Uses.dll" /></ItemGroup>""",
            "v4.6.1");

        Assert.Equal("Uses.dll", Names(references));
    }

    [Fact]
    public void AFrameworkAssemblyThatDependsOnSystemRuntimeExpandsNothing()
    {
        string framework = _fixture.Framework();
        _fixture.Library(Path.Combine(framework, "System.Web.dll"), "System.Web", "public class Page : RuntimeType { }", Path.Combine(framework, "Facades", "System.Runtime.dll"));

        ImmutableArray<ResolvedReference> references = Resolve(
            """<PropertyGroup><NoStdLib>true</NoStdLib><AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences></PropertyGroup><ItemGroup><Reference Include="System.Web" /></ItemGroup>""");

        Assert.Equal("System.Web.dll", Names(references));
    }

    [Fact]
    public void TheFrameworkBeatsItsFacadesAndTheFacadesBeatAFileOfTheSameName()
    {
        string framework = _fixture.Framework();
        _fixture.Library(Path.Combine(framework, "Dup.dll"), "Dup", "public class InFramework { }");
        _fixture.Library(Path.Combine(framework, "Facades", "Dup.dll"), "Dup", "public class InFacades { }");
        _fixture.Library(Path.Combine(framework, "Facades", "Two.dll"), "Two", "public class InFacades { }");
        _fixture.Write(Path.Combine("p", "Two"), "a file named like the reference");
        Directory.CreateDirectory(Path.Combine(_fixture.Root, "p", "folder"));

        ImmutableArray<ResolvedReference> references = Resolve("""
              <PropertyGroup><NoStdLib>true</NoStdLib><AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences></PropertyGroup>
              <ItemGroup>
                <Reference Include="Dup" />
                <Reference Include="Two" />
                <Reference Include="Folder"><HintPath>folder</HintPath></Reference>
              </ItemGroup>
            """);

        Assert.Equal([Path.Combine(framework, "Dup.dll"), Path.Combine(framework, "Facades", "Two.dll")], references.Select(static r => r.Path), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADependencyIsFollowedThroughTheFilesBesideAReference()
    {
        string framework = _fixture.Framework("v4.7.2");
        string runtime = Path.Combine(framework, "Facades", "System.Runtime.dll");
        string inner = _fixture.Library(Path.Combine("p", "lib", "Inner.dll"), "Inner", "public class Inner : RuntimeType { }", runtime);
        _fixture.Library(Path.Combine("p", "lib", "Outer.dll"), "Outer", "public class Outer { public Inner Value; }", inner);

        ImmutableArray<ResolvedReference> references = Resolve(
            """<PropertyGroup><NoStdLib>true</NoStdLib></PropertyGroup><ItemGroup><Reference Include="Outer"><HintPath>lib\Outer.dll</HintPath></Reference></ItemGroup>""",
            "v4.7.2");

        Assert.Contains("System.Runtime.dll", Names(references), StringComparison.Ordinal);
    }

    [Fact]
    public void AFileThatIsNotAManagedAssemblyDependsOnNothing()
    {
        _fixture.Framework();
        _fixture.Write(Path.Combine("p", "lib", "Text.dll"), "not a portable executable");
        _fixture.WriteBytes(Path.Combine("p", "lib", "Native.dll"), NativeImage());

        ImmutableArray<ResolvedReference> references = Resolve("""
              <PropertyGroup><NoStdLib>true</NoStdLib><AddAdditionalExplicitAssemblyReferences>false</AddAdditionalExplicitAssemblyReferences></PropertyGroup>
              <ItemGroup><Reference Include="lib\Text.dll" /><Reference Include="lib\Native.dll" /></ItemGroup>
            """);

        Assert.Equal("Text.dll|Native.dll", Names(references));
    }

    [Fact]
    public void NuspecFrameworkAssembliesAreReferences()
    {
        _fixture.Framework();
        string asset = _fixture.Library(Path.Combine("nuget", "web", "lib", "net45", "Web.dll"), "Web", "public class Web { }");
        ProjectAssets assets = new([asset, Path.Combine(_fixture.Root, "nuget", "missing.dll")], ["System.Numerics", "System.Nowhere"]);

        ImmutableArray<ResolvedReference> references = Resolve(string.Empty, assets: assets);

        Assert.Equal("mscorlib.dll|System.Numerics.dll|Web.dll|System.Core.dll", Names(references));
    }

    public void Dispose() => _fixture.Dispose();

    private static string Names(ImmutableArray<ResolvedReference> references) => string.Join('|', references.Select(static r => Path.GetFileName(r.Path)));

    /// <summary>A portable executable with a code section and no CLI header: a native DLL.</summary>
    private static byte[] NativeImage()
    {
        BlobBuilder image = new();
        new NativeBuilder().Serialize(image);
        return image.ToArray();
    }

    private ImmutableArray<ResolvedReference> Resolve(string body, string frameworkVersion = "v4.8", ProjectAssets? assets = null, string? shims = null)
    {
        EvaluatedProject project = MsBuildEvaluator.Evaluate(_fixture.Write(Path.Combine("p", "P.csproj"), BareFixture.Project(body, frameworkVersion)), _fixture.Variable);
        return AssemblyReferences.Resolve(
            project,
            Path.Combine(_fixture.ReferenceAssemblies, ".NETFramework", frameworkVersion),
            Version.Parse(frameworkVersion[1..]),
            assets ?? NoAssets,
            shims);
    }

    private sealed class NativeBuilder() : PEBuilder(PEHeaderBuilder.CreateLibraryHeader(), deterministicIdProvider: null)
    {
        protected override ImmutableArray<Section> CreateSections() => [new Section(".text", SectionCharacteristics.ContainsCode | SectionCharacteristics.MemRead)];

        protected override PEDirectoriesBuilder GetDirectories() => new();

        protected override BlobBuilder SerializeSection(string name, SectionLocation location)
        {
            BlobBuilder section = new();
            section.WriteBytes(0, 16);
            return section;
        }
    }
}
