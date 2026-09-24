using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Sorts compiler errors into "references did not resolve" (skip the project) and everything else (keep), and
/// workspace failures into real failures and MSBuild or NuGet warnings that the workspace reports as failures
/// (<see cref="MsBuildWarningCodes"/> and <see cref="ClassifyWorkspaceFailure"/>).
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

    /// <summary>
    /// A failure event is a warning when its message carries a code from <see cref="MsBuildWarningCodes"/>, or
    /// matches the shape of one of three NuGet restore compatibility warnings (P2-012). Unlike MSB3270, none of
    /// these carry their code in the text the workspace passes on (confirmed against a real restore: the
    /// workspace wraps the bare NuGet log message, dropping the "NUxxxx:" prefix a console logger would add), so
    /// each is matched by message shape instead of by code:
    /// <list type="bullet">
    /// <item><description><c>NU1701</c>: a package restored using a fallback framework (typically .NET Framework)
    /// because it has no asset for the project's target framework; common when a .NET Framework-only package is
    /// still referenced after a migration (<see cref="PackageRestoredForAFallbackFramework"/>).</description></item>
    /// <item><description><c>NU1702</c>: a <c>ProjectReference</c> resolved using a fallback framework, the same
    /// shape as NU1701 but for a project instead of a package
    /// (<see cref="ProjectReferenceResolvedForAFallbackFramework"/>).</description></item>
    /// <item><description><c>NU1903</c>: a NuGet audit finding (a package with a known vulnerability), not a load
    /// problem (<see cref="PackageHasAKnownVulnerability"/>).</description></item>
    /// </list>
    /// </summary>
    public static LoadDiagnosticKind ClassifyWorkspaceFailure(string message) =>
        MsBuildCode.Matches(message).Any(static m => MsBuildWarningCodes.Contains(m.Value))
        || PackageRestoredForAFallbackFramework.IsMatch(message)
        || ProjectReferenceResolvedForAFallbackFramework.IsMatch(message)
        || PackageHasAKnownVulnerability.IsMatch(message)
            ? LoadDiagnosticKind.WorkspaceWarning
            : LoadDiagnosticKind.WorkspaceFailure;

    [GeneratedRegex(@"\bMSB\d{4}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MsBuildCode { get; }

    [GeneratedRegex(
        @"\bPackage '[^']*' was restored using '[^']*' instead of the project target framework '[^']*'",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PackageRestoredForAFallbackFramework { get; }

    [GeneratedRegex(
        @"\bProjectReference '[^']*' was resolved using '[^']*' instead of the project target framework '[^']*'",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex ProjectReferenceResolvedForAFallbackFramework { get; }

    [GeneratedRegex(
        @"\bPackage '[^']*' \S+ has a known \w+ severity vulnerability\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PackageHasAKnownVulnerability { get; }
}
