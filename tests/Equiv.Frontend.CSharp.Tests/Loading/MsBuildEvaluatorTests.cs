using System.Xml;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class MsBuildEvaluatorTests : IDisposable
{
    private readonly BareFixture _fixture = new();

    [Fact]
    public void PropertiesAreReadInImportOrderUnderTheBuildHostsGlobals()
    {
        _fixture.Write(Path.Combine("p", "first.props"), """<Project><PropertyGroup><Order>$(Order)first;</Order></PropertyGroup></Project>""");
        EvaluatedProject project = Evaluate("""
              <PropertyGroup>
                <Order>start;</Order>
                <Configuration>Release</Configuration>
                <Skipped Condition="'$(Configuration)' == 'Release'">yes</Skipped>
                <Seen Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'">yes</Seen>
                <Design>$(DesignTimeBuild);$(BuildingInsideVisualStudio);$(OS);$(MSBuildRuntimeType)</Design>
              </PropertyGroup>
              <Import Project="first.props" />
              <PropertyGroup Condition="false"><Order>never</Order></PropertyGroup>
              <PropertyGroup><Order>$(Order)last</Order></PropertyGroup>
            """);

        Assert.Equal("start;first;last", project.Properties.Read("Order"));
        Assert.Equal("Debug", project.Properties.Read("Configuration"));
        Assert.Equal(string.Empty, project.Properties.Read("Skipped"));
        Assert.Equal("yes", project.Properties.Read("Seen"));
        Assert.Equal("true;true;Windows_NT;Full", project.Properties.Read("Design"));
        Assert.Equal("P", project.Properties.Read("MSBuildProjectName"));
        Assert.Equal(Path.Combine(_fixture.Root, "p"), project.Directory);
    }

    [Fact]
    public void ThisFilePropertiesNameTheImportWhileItIsReadAndAreRestoredAfter()
    {
        _fixture.Write(Path.Combine("p", "build", "inner.props"), """
            <Project><PropertyGroup><Inner>$(MSBuildThisFileDirectory)|$(MSBuildThisFile)|$(MSBuildThisFileName)|$(MSBuildThisFileFullPath)</Inner></PropertyGroup></Project>
            """);
        EvaluatedProject project = Evaluate("""
              <Import Project="build\inner.props" />
              <PropertyGroup><Outer>$(MSBuildThisFile)</Outer></PropertyGroup>
            """);

        string inner = Path.Combine(_fixture.Root, "p", "build", "inner.props");
        Assert.Equal($"{Path.GetDirectoryName(inner)}{Path.DirectorySeparatorChar}|inner.props|inner|{inner}", project.Properties.Read("Inner"));
        Assert.Equal("P.csproj", project.Properties.Read("Outer"));
    }

    [Fact]
    public void CommonPropsStandsForDirectoryBuildPropsAndTheRestoreGeneratedProps()
    {
        _fixture.Write("Directory.Build.props", """<Project><PropertyGroup><FromAbove>yes</FromAbove><Order>dbp;</Order></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "obj", "P.csproj.nuget.g.props"), """<Project><PropertyGroup><Order>$(Order)nuget;</Order></PropertyGroup></Project>""");
        EvaluatedProject project = Evaluate("""<PropertyGroup><Order>$(Order)project</Order></PropertyGroup>""");

        Assert.Equal("yes", project.Properties.Read("FromAbove"));
        Assert.Equal("dbp;nuget;project", project.Properties.Read("Order"));
        Assert.Equal(@"obj\", project.Properties.Read("BaseIntermediateOutputPath"));
    }

    [Fact]
    public void TheNearestDirectoryBuildPropsWins()
    {
        _fixture.Write("Directory.Build.props", """<Project><PropertyGroup><Which>outer</Which></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "directory.build.props"), """<Project><PropertyGroup><Which>inner</Which></PropertyGroup></Project>""");

        Assert.Equal("inner", Evaluate(string.Empty).Properties.Read("Which"));
    }

    [Fact]
    public void ImportSwitchesTurnTheImplicitImportsOff()
    {
        _fixture.Write("Directory.Build.props", """<Project><PropertyGroup><Seen>$(Seen)dbp;</Seen></PropertyGroup></Project>""");
        _fixture.Write("Directory.Build.targets", """<Project><PropertyGroup><Seen>$(Seen)dbt;</Seen></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "custom", "P.csproj.nuget.g.props"), """<Project><PropertyGroup><Seen>$(Seen)props;</Seen></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "custom", "P.csproj.nuget.g.targets"), """<Project><PropertyGroup><Seen>$(Seen)targets;</Seen></PropertyGroup></Project>""");
        string project = BareFixture.Project(string.Empty);
        string switches = """
              <PropertyGroup>
                <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
                <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
                <ImportProjectExtensionProps>false</ImportProjectExtensionProps>
                <ImportProjectExtensionTargets>false</ImportProjectExtensionTargets>
                <MSBuildProjectExtensionsPath>custom\</MSBuildProjectExtensionsPath>
              </PropertyGroup>
            """;
        string on = _fixture.Write(Path.Combine("p", "On.csproj"), project.Replace("<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">", "<Project><PropertyGroup><MSBuildProjectExtensionsPath>custom\\</MSBuildProjectExtensionsPath></PropertyGroup>", StringComparison.Ordinal).Replace("P.csproj", "On.csproj", StringComparison.Ordinal));
        string off = _fixture.Write(Path.Combine("p", "Off.csproj"), project.Replace("<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">", "<Project>" + switches, StringComparison.Ordinal));
        _fixture.Write(Path.Combine("p", "custom", "On.csproj.nuget.g.props"), """<Project><PropertyGroup><Seen>$(Seen)props;</Seen></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "custom", "On.csproj.nuget.g.targets"), """<Project><PropertyGroup><Seen>$(Seen)targets;</Seen></PropertyGroup></Project>""");

        Assert.Equal("dbp;props;dbt;targets;", MsBuildEvaluator.Evaluate(on, _fixture.Variable).Properties.Read("Seen"));
        Assert.Equal(string.Empty, MsBuildEvaluator.Evaluate(off, _fixture.Variable).Properties.Read("Seen"));
    }

    [Fact]
    public void CSharpTargetsStandsForTheUserFileDirectoryBuildTargetsAndTheRestoreGeneratedTargets()
    {
        _fixture.Write(Path.Combine("p", "P.csproj.user"), """<Project><PropertyGroup><Order>$(Order)user;</Order></PropertyGroup></Project>""");
        _fixture.Write("Directory.Build.targets", """<Project><PropertyGroup><Order>$(Order)dbt;</Order></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "obj", "P.csproj.nuget.g.targets"), """<Project><PropertyGroup><Order>$(Order)nuget</Order></PropertyGroup></Project>""");
        EvaluatedProject project = Evaluate("""<PropertyGroup><Order>project;</Order><Early>$(SolutionDir)</Early></PropertyGroup>""");

        Assert.Equal("project;user;dbt;nuget", project.Properties.Read("Order"));
        Assert.Equal(string.Empty, project.Properties.Read("Early"));
        Assert.Equal(MsBuildEvaluator.Undefined, project.Properties.Read("SolutionDir"));
        Assert.Equal(MsBuildEvaluator.Undefined, project.Properties.Read("SolutionPath"));
    }

    [Fact]
    public void RelativeImportsAreFollowedOnceAndMissingOnesConditionedOnExistsAreIgnored()
    {
        _fixture.Write(Path.Combine("p", "once.props"), """<Project><PropertyGroup><Count>$(Count)x</Count></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "parts", "a.props"), """<Project><PropertyGroup><Parts>$(Parts)a</Parts></PropertyGroup></Project>""");
        _fixture.Write(Path.Combine("p", "parts", "b.props"), """<Project><PropertyGroup><Parts>$(Parts)b</Parts></PropertyGroup></Project>""");
        EvaluatedProject project = Evaluate("""
              <Import Project="once.props" />
              <Import Project="ONCE.props" />
              <Import Project="parts\*.props" />
              <Import Project="missing.targets" Condition="Exists('missing.targets')" />
              <Import Project="missing.targets" Condition="'$(Unset)' == '' and !Exists('elsewhere')" />
              <Import Project="skipped.targets" Condition="false" />
              <ImportGroup Condition="false"><Import Project="never.targets" /></ImportGroup>
              <ImportGroup><Import Project="once.props" /><Other /></ImportGroup>
            """);

        Assert.Equal("x", project.Properties.Read("Count"));
        Assert.Equal("ab", project.Properties.Read("Parts"));
    }

    [Theory]
    [InlineData("""<Import Project="$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets" />""")]
    [InlineData("""<Import Project="$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v10.0\WebApplications\Microsoft.WebApplication.targets" Condition="$([MSBuild]::Anything())" />""")]
    [InlineData("""<PropertyGroup><Web>$(MSBuildExtensionsPath32)\Web</Web></PropertyGroup><Import Project="$(Web)\Microsoft.WebApplication.targets" />""")]
    public void OtherToolPathImportsAreReplacedByNothing(string import) =>
        Assert.Empty(Evaluate(import).Items);

    [Theory]
    [InlineData("""<Choose><When Condition="true" /></Choose>""", "<Choose>")]
    [InlineData("""<Import Project="missing.targets" />""", "the missing import 'missing.targets'")]
    [InlineData("""<Import Project="missing.targets" Condition="'$(Unset)' == ''" />""", "the missing import 'missing.targets'")]
    [InlineData("""<Import Project="$([MSBuild]::GetPathOfFileAbove('x.props'))" />""", "the property function $([MSBuild]::GetPathOfFileAbove('x.props'))")]
    [InlineData("""<Import Project="x.props" Condition="'a' =~ 'b'" />""", "a condition outside the supported grammar: \"'a' =~ 'b'\"")]
    [InlineData("""<ImportGroup Condition="'a' =~ 'b'" />""", "a condition outside the supported grammar: \"'a' =~ 'b'\"")]
    [InlineData("""<Target Name="Gen"><ItemGroup><Compile Include="gen.cs" /></ItemGroup></Target>""", "a <Target> that creates Compile items (target 'Gen')")]
    [InlineData("""<Target Name="Refs"><CreateItem Include="x.dll"><Output TaskParameter="Include" ItemName="Reference" /></CreateItem></Target>""", "a <Target> that creates Reference items (target 'Refs')")]
    [InlineData("""<Target Name="P"><ItemGroup><ProjectReference Include="x.csproj" /></ItemGroup></Target>""", "a <Target> that creates ProjectReference items (target 'P')")]
    [InlineData("""<ItemGroup><COMReference Include="Word" /></ItemGroup>""", "<COMReference>")]
    [InlineData("""<ItemGroup><Compile Include="a.cs" Condition="'a' =~ 'b'" /></ItemGroup>""", "a condition outside the supported grammar: \"'a' =~ 'b'\"")]
    [InlineData("""<ItemGroup><Compile Include="@(Other)" /></ItemGroup>""", "an item reference or metadata in '@(Other)'")]
    [InlineData("""<ItemGroup><Reference Include="A"><HintPath>$([System.IO.Path]::Combine('a','b'))</HintPath></Reference></ItemGroup>""", "the property function $([System.IO.Path]::Combine('a','b'))")]
    public void AConstructTheEvaluatorCannotEvaluateExactlyThrowsNamingIt(string body, string construct) =>
        Assert.Equal(construct, Assert.Throws<UnsupportedConstructException>(() => Evaluate(body)).Construct);

    [Fact]
    public void ATargetThatCreatesOtherItemsIsHarmless()
    {
        EvaluatedProject project = Evaluate("""
              <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
                <PropertyGroup><ErrorText>$([System.String]::Format('{0}', 'x'))</ErrorText></PropertyGroup>
                <ItemGroup><Content Include="x.txt" /><Compile Remove="a.cs" /></ItemGroup>
                <CreateItem Include="y"><Output TaskParameter="Include" ItemName="Content" /><Output TaskParameter="Include" PropertyName="Y" /></CreateItem>
                <Error Condition="!Exists('x')" Text="$(ErrorText)" />
              </Target>
            """);

        Assert.Empty(project.Items);
    }

    [Fact]
    public void AnUnsupportedConditionOnAPropertyPoisonsOnlyThatPropertyAndWhatReadsIt()
    {
        EvaluatedProject project = Evaluate("""
              <PropertyGroup>
                <Guarded Condition="'a' =~ 'b'">x</Guarded>
                <Computed>$([System.DateTime]::Now)</Computed>
                <Uses>$(Guarded)</Uses>
                <Fine>fine</Fine>
              </PropertyGroup>
              <PropertyGroup Condition="$(Guarded) == ''"><Group>y</Group></PropertyGroup>
              <ItemGroup Condition="'a' =~ 'b'"><Content Include="x" /></ItemGroup>
            """);

        const string Guarded = "a condition outside the supported grammar: \"'a' =~ 'b'\"";
        Assert.Equal(Guarded, Assert.Throws<UnsupportedConstructException>(() => project.Properties.Read("Guarded")).Construct);
        Assert.Equal(Guarded, Assert.Throws<UnsupportedConstructException>(() => project.Properties.Read("Uses")).Construct);
        Assert.Equal(Guarded, Assert.Throws<UnsupportedConstructException>(() => project.Properties.Read("Group")).Construct);
        Assert.Equal("the property function $([System.DateTime]::Now)", Assert.Throws<UnsupportedConstructException>(() => project.Properties.Read("Computed")).Construct);
        Assert.Equal("fine", project.Properties.Read("Fine"));
    }

    [Fact]
    public void CompileItemsExpandWildcardsHonourExcludeAndRemoveAndIgnoreUpdate()
    {
        foreach (string file in (string[])["A.cs", "B.cs", "Gen.g.cs", Path.Combine("Sub", "C.cs")])
        {
            _fixture.Write(Path.Combine("p", file), string.Empty);
        }

        EvaluatedProject project = Evaluate("""
              <PropertyGroup><Pattern>**\*.cs</Pattern></PropertyGroup>
              <ItemGroup>
                <Compile Include="$(Pattern)" Exclude="*.g.cs" />
                <Compile Include="a.cs;Missing.cs" />
                <Compile Update="B.cs" />
                <Compile Remove="sub\*.cs" />
                <Compile Include="Skipped.cs" Condition="false" />
                <Content Include="x.txt" />
              </ItemGroup>
              <ItemGroup Condition="false"><Compile Include="Never.cs" /></ItemGroup>
            """);

        string directory = Path.Combine(_fixture.Root, "p");
        Assert.Equal(
            [Path.Combine(directory, "A.cs"), Path.Combine(directory, "B.cs"), Path.Combine(directory, "A.cs"), Path.Combine(directory, "Missing.cs")],
            project.OfType("compile").Select(static c => c.Include),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferenceItemsCarryTheMetadataTheLoaderReads()
    {
        EvaluatedProject project = Evaluate("""
              <PropertyGroup><Packages>..\packages</Packages></PropertyGroup>
              <ItemGroup>
                <Reference Include="Newtonsoft.Json, Version=12.0.0.0, Culture=neutral" Aliases="json">
                  <HintPath>$(Packages)\Newtonsoft.Json.12.0.1\lib\net45\Newtonsoft.Json.dll</HintPath>
                  <Private>True</Private>
                  <EmbedInteropTypes Condition="false">True</EmbedInteropTypes>
                </Reference>
                <Reference Include="System;System.Core" />
                <Reference Include="Gone" />
                <Reference Remove="gone" />
                <ProjectReference Include="..\Lib\Lib.csproj"><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference>
                <PackageReference Include="Some.Package" Version="1.0.0" />
              </ItemGroup>
            """);

        EvaluatedItem json = project.OfType("Reference").First();
        Assert.Equal("Newtonsoft.Json, Version=12.0.0.0, Culture=neutral", json.Include);
        Assert.Equal(@"..\packages\Newtonsoft.Json.12.0.1\lib\net45\Newtonsoft.Json.dll", json.Metadatum("HintPath"));
        Assert.Equal("json", json.Metadatum("Aliases"));
        Assert.Null(json.Metadatum("EmbedInteropTypes"));
        Assert.Null(json.Metadatum("Private"));
        Assert.Equal(["Newtonsoft.Json, Version=12.0.0.0, Culture=neutral", "System", "System.Core"], project.OfType("Reference").Select(static r => r.Include), StringComparer.Ordinal);
        EvaluatedItem library = Assert.Single(project.OfType("ProjectReference"));
        Assert.Equal(Path.Combine(_fixture.Root, "Lib", "Lib.csproj"), library.Include);
        Assert.Equal("false", library.Metadatum("ReferenceOutputAssembly"));
        Assert.Equal("Some.Package", Assert.Single(project.OfType("PackageReference")).Include);
    }

    [Fact]
    public void AMalformedImportIsAnXmlError()
    {
        _fixture.Write(Path.Combine("p", "broken.props"), "<Project>");

        Assert.Throws<XmlException>(() => Evaluate("""<Import Project="broken.props" />"""));
    }

    public void Dispose() => _fixture.Dispose();

    private EvaluatedProject Evaluate(string body) =>
        MsBuildEvaluator.Evaluate(_fixture.Write(Path.Combine("p", "P.csproj"), BareFixture.Project(body)), _fixture.Variable);
}
