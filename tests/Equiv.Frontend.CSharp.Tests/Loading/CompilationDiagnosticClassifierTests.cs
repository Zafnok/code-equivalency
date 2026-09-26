using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class CompilationDiagnosticClassifierTests
{
    [Theory]
    [InlineData("CS0006")] // metadata file not found
    [InlineData("CS0012")] // type in unreferenced assembly
    [InlineData("CS1705")] // referenced assembly version too high
    public void Classify_MissingOrMismatchedAssemblyIsUnresolvedReference(string id)
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify(id));
    }

    [Theory]
    [InlineData("CS0234")] // namespace member missing
    [InlineData("CS0246")] // type or namespace not found
    [InlineData("CS0400")] // not found in global namespace
    public void Classify_UnboundTypeOrNamespaceIsUnresolvedReference(string id)
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify(id));
    }

    [Fact]
    public void Classify_MissingPredefinedTypeIsUnresolvedReference()
    {
        // CS0518 is how a missing .NET Framework 4.8 targeting pack shows up (ticket Pitfalls).
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify("CS0518"));
    }

    [Fact]
    public void Classify_AnalyzerLoadFailureIsUnresolvedReference()
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify("CS8032"));
    }

    [Theory]
    [InlineData("CS0029")] // cannot implicitly convert
    [InlineData("CS0103")] // name does not exist in the current context
    [InlineData("CS1002")] // ; expected
    public void Classify_OtherErrorIsCompilerError(string id)
    {
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify(id));
    }

    [Fact]
    public void Classify_IsCaseSensitive()
    {
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify("cs0246"));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_ProcessorArchitectureMismatchIsAWarning()
    {
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(@"C:\x\A.csproj : warning MSB3270: There was a mismatch between the processor architecture"));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_PackageRestoredForTheWrongFrameworkIsAWarning()
    {
        // NU1701: a package with no net10.0-compatible asset restored against a .NET Framework fallback.
        // MSBuildWorkspace wraps the bare NuGet log message and drops its "NU1701:" code (verified against a
        // real restore), so this is matched by shape, not by code.
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\A.csproj' with message: Package 'Old.Widgets 2.1.0' was restored using '.NETFramework,Version=v4.8' instead of the project target framework 'net10.0'. It may not work."));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_KnownVulnerablePackageIsAWarning()
    {
        // NU1903: a NuGet audit finding, not a load problem. Also carries no code in the wrapped message.
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\A.csproj' with message: Package 'Old.Widgets' 2.1.0 has a known moderate severity vulnerability, https://example.invalid/advisories/GHSA-0000-0000-0000"));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_ProjectReferenceResolvedForTheWrongFrameworkIsAWarning()
    {
        // NU1702: the same fallback shape as NU1701, but for a ProjectReference instead of a package.
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\Modern.csproj' with message: ProjectReference 'C:\x\Adapters.csproj' was resolved using '.NETFramework,Version=v4.8' instead of the project target framework 'netstandard2.0'. Compilation may fail."));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_RedundantImplicitFrameworkReferenceIsAWarning()
    {
        // NETSDK1086: a project lists a FrameworkReference the SDK already adds implicitly. The workspace passes
        // the SDK message on without its code (seen in M3-028), so this is matched by shape, not by code.
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\Desktop.csproj' with message: A FrameworkReference for 'Microsoft.WindowsDesktop.App' was included in the project. This is implicitly referenced by the .NET SDK and you do not typically need to reference it from your project."));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_MissingRuntimeIdentifierIsAWarning()
    {
        // NU1004: a non-SDK .NET Framework project's implicit restore wants a RuntimeIdentifiers entry for a
        // package with a runtimes/win/... asset (P2-021). The corpus skill's own restore already resolved the
        // packages, so this never reaches the compilation.
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\Programmerare.ShortestPaths.Test.csproj' with message: Your project file doesn't list 'win' as a ""RuntimeIdentifier"". You should add 'win' to the 'RuntimeIdentifiers' property in your project file and then re-run NuGet restore."));
    }

    [Fact]
    public void ClassifyWorkspaceFailure_MissingRuntimeIdentifierIsAWarningWithSingleQuotedWording()
    {
        Assert.Equal(
            LoadDiagnosticKind.WorkspaceWarning,
            CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(
                @"Msbuild failed when processing the file 'C:\x\A.csproj' with message: Your project file doesn't list 'win-x64' as a 'RuntimeIdentifier'. You should add 'win-x64' to the 'RuntimeIdentifiers' property in your project file and then re-run NuGet restore."));
    }

    [Theory]
    [InlineData("error MSB4019: The imported project was not found")]
    [InlineData("warning MSB32701: not the same code")]
    [InlineData("project file could not be evaluated")]
    [InlineData("Package 'Old.Widgets' 2.1.0 has a known vulnerability, but not the exact wording")]
    [InlineData("a ProjectReference was resolved, but not against a target framework at all")]
    [InlineData("A FrameworkReference for 'Microsoft.AspNetCore.App' was included in the project, but it could not be resolved")]
    [InlineData("Your project file doesn't reference a RuntimeIdentifier at all")]
    public void ClassifyWorkspaceFailure_AnythingElseIsAFailure(string message)
    {
        Assert.Equal(LoadDiagnosticKind.WorkspaceFailure, CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(message));
    }
}
