using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

/// <summary>Ticket P2-053 acceptance criteria 2 and 3 (ADR 0040 decision 1).</summary>
public sealed class RuntimeDetectionTests
{
    private const string Standard = ".NETStandard,Version=v2.0";
    private const string Framework = ".NETFramework,Version=v4.8";
    private const string Core = ".NETCoreApp,Version=v8.0";

    // CoreLib only: the test host's trusted platform assemblies include xunit, which would make every project a test project.
    private static readonly MetadataReference CoreLibrary = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    private static CSharpCompilation Project(string name, string? moniker, OutputKind kind = OutputKind.DynamicallyLinkedLibrary, params Compilation[] references) =>
        CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(moniker is null ? "class C { }" : $"[assembly: System.Runtime.Versioning.TargetFramework(\"{moniker}\")] class C {{ }}", cancellationToken: TestContext.Current.CancellationToken)],
            [CoreLibrary, .. references.Select(static r => r.ToMetadataReference())],
            new CSharpCompilationOptions(kind));

    private static ImmutableArray<(string Project, string Runtime, string Source)> Detect(TargetRuntime? configured, params Compilation[] compilations) =>
        [.. RuntimeDetection.Detect([.. compilations], configured).Select(static r => (r.Project, r.Runtime, r.Source))];

    [Fact]
    public void ReadsTheTargetFrameworkAttribute()
    {
        Compilation legacy = Project("Legacy", Framework);
        Compilation modern = Project("Modern", Core);

        Assert.Equal(Framework, RuntimeDetection.Declared(legacy));
        Assert.Null(RuntimeDetection.Declared(Project("None", moniker: null)));
        Assert.Null(RuntimeDetection.Declared(CSharpCompilation.Create(
            "NullMoniker",
            [CSharpSyntaxTree.ParseText("[assembly: System.Runtime.Versioning.TargetFramework(null)]", cancellationToken: TestContext.Current.CancellationToken)],
            [CoreLibrary])));
        Assert.Equal(
            [("Legacy", "net48", RuntimeDetection.Attribute), ("Modern", "net8.0", RuntimeDetection.Attribute)],
            Detect(configured: null, modern, legacy));

        // P2-016: a multi-targeted project is its last flavour.
        Assert.Equal([("Multi", "net8.0", RuntimeDetection.Attribute)], Detect(configured: null, Project("Multi", Framework), Project("Multi", Core)));
    }

    [Theory]
    [InlineData(OutputKind.ConsoleApplication)]
    [InlineData(OutputKind.WindowsApplication)]
    [InlineData(OutputKind.WindowsRuntimeApplication)]
    public void NetStandardTakesItsHostsRuntimes(OutputKind executable)
    {
        Compilation standard = Project("Std", Standard);
        Compilation library = Project("Lib", Framework, OutputKind.DynamicallyLinkedLibrary, standard);
        Compilation app = Project("App", Framework, executable, library, standard);
        Compilation xunit = Project("xunit.core", Standard);
        Compilation tests = Project("Tests", Core, OutputKind.DynamicallyLinkedLibrary, xunit, standard);
        Compilation other = Project("Other", ".NETCoreApp,Version=v10.0", OutputKind.DynamicallyLinkedLibrary, standard);
        Compilation second = Project("Second", Framework, executable, standard);
        Compilation lone = Project("Lone", Standard);
        Compilation unknownHost = Project("UnknownHost", moniker: null, executable, lone);

        ImmutableArray<(string Project, string Runtime, string Source)> runtimes = Detect(TargetRuntime.Parse("net10.0"), standard, library, app, tests, other, second, lone, unknownHost);

        Assert.Contains(("Std", "net48, net8.0", RuntimeDetection.Host), runtimes);
        Assert.Contains(("Lone", "net10.0", RuntimeDetection.Config), runtimes);
    }

    [Fact]
    public void UnhostedNetStandardUsesConfig()
    {
        Compilation standard = Project("Std", Standard);
        Compilation library = Project("Lib", Framework, OutputKind.DynamicallyLinkedLibrary, standard);

        Assert.Equal(
            [("Lib", "net48", RuntimeDetection.Attribute), ("Std", "net48", RuntimeDetection.Config)],
            Detect(TargetRuntime.Parse("net48"), standard, library));
    }

    [Fact]
    public void UnhostedWithoutConfig() =>
        Assert.Equal(
            [
                ("Bare", ".NETStandard", RuntimeDetection.Unhosted),
                ("None", RuntimeDetection.Unknown, RuntimeDetection.Unhosted),
                ("Portable", ".NETPortable,Version=v4.5,Profile=Profile7", RuntimeDetection.Unhosted),
                ("Std", "netstandard2.0", RuntimeDetection.Unhosted),
            ],
            Detect(
                configured: null,
                Project("Std", Standard),
                Project("None", moniker: null),
                Project("Portable", ".NETPortable,Version=v4.5,Profile=Profile7"),
                Project("Bare", ".NETStandard")));
}
