using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class CompilationDiagnosticClassifierTests
{
    /// <summary>A reference that is itself broken skips the project on either side (ADR 0029 decision 1).</summary>
    [Theory]
    [InlineData("CS0006", Codebase.Legacy)] // metadata file not found
    [InlineData("CS0006", Codebase.Modern)]
    [InlineData("CS1705", Codebase.Legacy)] // referenced assembly version too high
    [InlineData("CS1705", Codebase.Modern)]
    public void Classify_MissingOrMismatchedAssemblyIsUnresolvedReference(string id, Codebase side)
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify(id, side));
    }

    /// <summary>On the legacy side a name no reference provides still means the project was loaded wrongly (M3-024).</summary>
    [Theory]
    [InlineData("CS0012")] // type in unreferenced assembly
    [InlineData("CS0234")] // namespace member missing
    [InlineData("CS0246")] // type or namespace not found
    [InlineData("CS0400")] // not found in global namespace
    public void Classify_UnboundTypeOrNamespaceIsUnresolvedReferenceOnTheLegacySide(string id)
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify(id, Codebase.Legacy));
    }

    /// <summary>
    /// Ticket P2-085 (ADR 0029 as clarified): on the modern side the same errors are what a migration tool's raw output
    /// looks like, so the project is kept and each method that does not bind is Unknown(Unbound).
    /// </summary>
    [Theory]
    [InlineData("CS0012")] // type in unreferenced assembly
    [InlineData("CS0234")] // namespace member missing
    [InlineData("CS0246")] // type or namespace not found
    [InlineData("CS0400")] // not found in global namespace
    public void Classify_UnboundTypeOrNamespaceIsACompilerErrorOnTheModernSide(string id)
    {
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify(id, Codebase.Modern));
    }

    [Theory]
    [InlineData(Codebase.Legacy)]
    [InlineData(Codebase.Modern)]
    public void Classify_MissingPredefinedTypeIsUnresolvedReference(Codebase side)
    {
        // CS0518 is how a missing .NET Framework 4.8 targeting pack shows up (ticket Pitfalls): with no core library nearly nothing binds.
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify("CS0518", side));
    }

    [Theory]
    [InlineData(Codebase.Legacy)]
    [InlineData(Codebase.Modern)]
    public void Classify_AnalyzerLoadFailureIsUnresolvedReference(Codebase side)
    {
        Assert.Equal(LoadDiagnosticKind.UnresolvedReference, CompilationDiagnosticClassifier.Classify("CS8032", side));
    }

    [Theory]
    [InlineData("CS0029", Codebase.Legacy)] // cannot implicitly convert
    [InlineData("CS0103", Codebase.Legacy)] // name does not exist in the current context
    [InlineData("CS1002", Codebase.Legacy)] // ; expected
    [InlineData("CS0103", Codebase.Modern)]
    [InlineData("CS1002", Codebase.Modern)]
    public void Classify_OtherErrorIsCompilerError(string id, Codebase side)
    {
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify(id, side));
    }

    /// <summary>
    /// Ticket P2-106 (ADR 0029 as clarified): an error is a diagnostic the compiler itself calls one. A warning the project
    /// promotes (<c>TreatWarningsAsErrors</c>, <c>WarningsAsErrors</c>) is reported with severity error and is not one,
    /// whatever its id: CS8032 is a warning by default, so promoted it never reaches <c>Classify</c> to skip a project.
    /// </summary>
    [Theory]
    [InlineData("CS0103", DiagnosticSeverity.Error, DiagnosticSeverity.Error, true)]
    [InlineData("CS0618", DiagnosticSeverity.Error, DiagnosticSeverity.Warning, false)]
    [InlineData("CS8032", DiagnosticSeverity.Error, DiagnosticSeverity.Warning, false)]
    [InlineData("CS0618", DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, false)]
    [InlineData("CS8019", DiagnosticSeverity.Hidden, DiagnosticSeverity.Hidden, false)]
    public void IsError_IsTheCompilersOwnSeverityNotTheProjects(string id, DiagnosticSeverity reported, DiagnosticSeverity byDefault, bool expected)
    {
        Diagnostic diagnostic = Diagnostic.Create(id, "Compiler", "message", reported, byDefault, isEnabledByDefault: true, warningLevel: reported == DiagnosticSeverity.Error ? 0 : 1);

        Assert.Equal(expected, CompilationDiagnosticClassifier.IsError(diagnostic));
    }

    [Fact]
    public void Classify_IsCaseSensitive()
    {
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify("cs0246", Codebase.Legacy));
        Assert.Equal(LoadDiagnosticKind.CompilerError, CompilationDiagnosticClassifier.Classify("cs0518", Codebase.Modern));
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
