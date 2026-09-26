using System.Collections.Immutable;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Corpus.Seeder;

/// <summary>
/// Whether mutating one file introduced a compile error the original file did not already have (ticket M4-010
/// acceptance criterion 3). A corpus file rarely compiles standalone (it depends on sibling files and NuGet
/// packages this tool never loads, since it does Roslyn syntax rewriting only, no MSBuild or semantic model beyond
/// the BCL), so both the original and the mutated text are compiled the same way and only the *new* errors count:
/// unresolved-symbol errors from missing project context appear identically on both sides and are not the mutation's
/// fault.
/// </summary>
internal static class CompileCheck
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(static () =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))]);

    /// <summary><see langword="true"/> when <paramref name="mutated"/> compiles no worse than <paramref name="original"/> did.</summary>
    public static bool StillCompiles(string original, string mutated) => ErrorCount(mutated) <= ErrorCount(original);

    private static int ErrorCount(string source)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, cancellationToken: CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create("EquivSeederCheck", [tree], References.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics().Count(static d => d.Severity == DiagnosticSeverity.Error);
    }
}
