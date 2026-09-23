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

    [Theory]
    [InlineData("error MSB4019: The imported project was not found")]
    [InlineData("warning MSB32701: not the same code")]
    [InlineData("project file could not be evaluated")]
    public void ClassifyWorkspaceFailure_AnythingElseIsAFailure(string message)
    {
        Assert.Equal(LoadDiagnosticKind.WorkspaceFailure, CompilationDiagnosticClassifier.ClassifyWorkspaceFailure(message));
    }
}
