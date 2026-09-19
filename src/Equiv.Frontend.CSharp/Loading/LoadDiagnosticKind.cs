namespace Equiv.Frontend.CSharp.Loading;

internal enum LoadDiagnosticKind
{
    /// <summary>A <c>WorkspaceDiagnosticKind.Failure</c> event; aborts the load.</summary>
    WorkspaceFailure,

    /// <summary>A <c>WorkspaceDiagnosticKind.Warning</c> event; kept.</summary>
    WorkspaceWarning,

    /// <summary>A compiler error meaning references did not resolve; aborts the load.</summary>
    UnresolvedReference,

    /// <summary>Any other compiler error; kept, since the methods that bind are still usable.</summary>
    CompilerError,

    /// <summary>The solution has no C# project, or has a project in another language; aborts the load.</summary>
    UnsupportedSolution,
}
