using System.Collections.Frozen;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>Sorts compiler errors into "references did not resolve" (abort) and everything else (keep).</summary>
internal static class CompilationDiagnosticClassifier
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

    public static LoadDiagnosticKind Classify(string errorId) =>
        UnresolvedReferenceIds.Contains(errorId) ? LoadDiagnosticKind.UnresolvedReference : LoadDiagnosticKind.CompilerError;
}
