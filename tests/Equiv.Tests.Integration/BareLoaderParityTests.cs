using System.Collections.Immutable;

using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// M3-029 acceptance criterion 2: on Windows, every sample's legacy side loads the same through the bare loader (behind
/// <see cref="CompositeSolutionLoader"/>, which sends a non-SDK project there) as through
/// <see cref="MsBuildSolutionLoader"/>: per project, the same status, source paths relative to the solution, reference
/// file names, error ids with their locations, and <c>Options.Platform</c>. This keeps the two legacy loaders in step
/// (M3-028's Decision); the <c>parity</c> CI job checks the same on Linux end to end.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BareLoaderParityTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples => [.. Directory.GetDirectories(SamplesRoot).Select(static d => Path.GetFileName(d)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task BareLoaderMatchesMsBuildOnEverySampleLegacySide(string sample)
    {
        string solutionPath = Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single();
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;

        LoadedSolution msBuild = await new MsBuildSolutionLoader().LoadAsync(solutionPath, TestContext.Current.CancellationToken);
        LoadedSolution bare = await new CompositeSolutionLoader().LoadAsync(solutionPath, TestContext.Current.CancellationToken);

        Assert.Equal(Describe(msBuild, solutionDirectory), Describe(bare, solutionDirectory));
    }

    /// <summary>One line per project, fact by fact, so a mismatch shows its first difference in the assertion message.</summary>
    private static ImmutableArray<string> Describe(LoadedSolution loaded, string solutionDirectory) =>
    [
        .. loaded.Compilations.Select(c => (Status: "loaded", Compilation: c))
            .Concat(loaded.Skipped.Where(static s => s.Compilation is not null).Select(static s => (Status: "skipped", Compilation: s.Compilation!)))
            .OrderBy(static p => p.Compilation.AssemblyName, StringComparer.Ordinal)
            .SelectMany(p => Facts(p.Status, p.Compilation, solutionDirectory)),
        .. loaded.Skipped.Where(static s => s.Compilation is null).Select(static s => $"{s.Name}: skipped without a compilation"),
    ];

    private static IEnumerable<string> Facts(string status, Compilation compilation, string solutionDirectory)
    {
        string project = compilation.AssemblyName!;
        yield return $"{project}: {status}";
        yield return $"{project}: platform {compilation.Options.Platform}";
        foreach (string source in compilation.SyntaxTrees.Select(t => Relative(solutionDirectory, t.FilePath)).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return $"{project}: source {source}";
        }

        foreach (string reference in compilation.References.Select(Reference).Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return $"{project}: reference {reference}";
        }

        foreach (string error in compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {Relative(solutionDirectory, d.Location.GetLineSpan().Path)} {d.Location.GetLineSpan().Span}")
            .Order(StringComparer.Ordinal))
        {
            yield return $"{project}: error {error}";
        }
    }

    private static string Reference(MetadataReference reference) => reference switch
    {
        CompilationReference project => $"project:{project.Compilation.AssemblyName}",
        _ => Path.GetFileName(((PortableExecutableReference)reference).FilePath!).ToUpperInvariant(),
    };

    private static string Relative(string root, string path) =>
        path.Length == 0 ? "<none>" : Path.GetRelativePath(root, path).Replace('\\', '/').ToUpperInvariant();
}
