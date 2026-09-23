namespace Equiv.Frontend.CSharp.Loading;

internal enum LoadDiagnosticKind
{
    /// <summary>A <c>WorkspaceDiagnosticKind.Failure</c> event; skips the C# project it names.</summary>
    WorkspaceFailure,

    /// <summary>
    /// A <c>WorkspaceDiagnosticKind.Warning</c> event, or a failure event whose message carries a known MSBuild
    /// warning code (<see cref="CompilationDiagnosticClassifier.ClassifyWorkspaceFailure"/>); kept.
    /// </summary>
    WorkspaceWarning,

    /// <summary>A compiler error meaning references did not resolve; skips the project.</summary>
    UnresolvedReference,

    /// <summary>Any other compiler error; kept, since the methods that bind are still usable.</summary>
    CompilerError,

    /// <summary>A project that is not C#; skipped with a warning.</summary>
    UnsupportedProject,

    /// <summary>No C# project loaded; the only diagnostic that aborts the load.</summary>
    UnsupportedSolution,
}
