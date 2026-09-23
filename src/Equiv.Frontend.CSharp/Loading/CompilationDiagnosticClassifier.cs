using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Sorts compiler errors into "references did not resolve" (skip the project) and everything else (keep), and
/// workspace failures into real failures and MSBuild warnings that the workspace reports as failures.
/// </summary>
internal static partial class CompilationDiagnosticClassifier
{
    private static readonly FrozenSet<string> UnresolvedReferenceIds = FrozenSet.Create(
        StringComparer.Ordinal,
        "CS0006", // metadata file could not be found
        "CS0012", // type defined in an assembly that is not referenced
        "CS0234", // type or namespace does not exist in the namespace
        "CS0246", // type or namespace could not be found
        "CS0400", // type or namespace could not be found in the global namespace
        "CS0518", // predefined type is not defined (missing targeting pack)
        "CS1705", // referenced assembly has a higher version
        "CS8032"); // analyzer instance could not be created

    /// <summary>
    /// MSBuild warning codes that MSBuildWorkspace has been seen to report as a <c>WorkspaceDiagnosticKind.Failure</c>.
    /// They skip nothing (M3-024 acceptance criterion 9).
    /// </summary>
    private static readonly FrozenSet<string> MsBuildWarningCodes = FrozenSet.Create(
        StringComparer.Ordinal,
        "MSB3270"); // processor-architecture mismatch between the project and a reference

    public static LoadDiagnosticKind Classify(string errorId) =>
        UnresolvedReferenceIds.Contains(errorId) ? LoadDiagnosticKind.UnresolvedReference : LoadDiagnosticKind.CompilerError;

    /// <summary>A failure event is a warning when its message carries a code from the MSBuild warning table.</summary>
    public static LoadDiagnosticKind ClassifyWorkspaceFailure(string message) =>
        MsBuildCode.Matches(message).Any(static m => MsBuildWarningCodes.Contains(m.Value))
            ? LoadDiagnosticKind.WorkspaceWarning
            : LoadDiagnosticKind.WorkspaceFailure;

    [GeneratedRegex(@"\bMSB\d{4}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MsBuildCode { get; }
}
