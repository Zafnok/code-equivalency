using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

/// <summary>Ticket M3-014 acceptance criteria 7 and 9: the analysed line count, per side, by README's counting rule.</summary>
public sealed class CodeLinesTests
{
    // Counted, by line: 1, 4, 5, 8, 9, 11, 16, 17, 18, 19, 20 (11 lines). Not counted: the blank line 2, the
    // comments on lines 3, 6 and 7, the directives on lines 10, 12, 13 and 15, and line 14, which #if excludes.
    // Line 11 counts once however many tokens it holds; the verbatim string counts every line it spans.
    private const string Legacy = """
        using System;

        // A comment-only line.
        namespace N
        {
            /* A block comment
               over two lines. */
            public class C
            {
        #region Members
                public int M(int a) { int b = a; return b; } // A trailing comment.
        #endregion
        #if NEVER
                public int Excluded() { return 0; }
        #endif
                public string S() => @"one
        two
        three";
            }
        }

        """;

    // Every one of its 4 lines holds a token; the trailing newline ends the file without adding a line.
    private const string Modern = """
        namespace N
        {
            public class C { public int M(int a) => a; }
        }

        """;

    private sealed class StubLoader(LoadedSolution legacy, LoadedSolution modern) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) =>
            Task.FromResult(string.Equals(solutionPath, "legacy.sln", StringComparison.Ordinal) ? legacy : modern);
    }

    [Fact]
    public void AFixturePairCountsTheHandComputedLinesOnEachSide()
    {
        CSharpFrontend frontend = new(
            new StubLoader(Solution(Compilation(("Legacy.cs", Legacy))), Solution(Compilation(("Modern.cs", Modern)))),
            new StableIdentityMatcher());

        AnalysedLines lines = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, CancellationToken.None).Lines;

        Assert.Equal(new AnalysedLines(Legacy: 11, Modern: 4), lines);
    }

    [Fact]
    public void AFileTwoProjectsCompileCountsOnce()
    {
        ImmutableArray<Compilation> compilations =
        [
            Compilation(("Shared.cs", Modern), ("Own.cs", Modern)),
            Compilation(("Shared.cs", Modern)),
        ];

        Assert.Equal(8, CodeLines.Count(compilations));
    }

    [Fact]
    public void AnEmptyFileHasNoLines() =>
        Assert.Equal(0, CodeLines.Count(CSharpSyntaxTree.ParseText("// Only a comment.\n\n", cancellationToken: TestContext.Current.CancellationToken)));

    private static LoadedSolution Solution(Compilation compilation) => new(null!, [compilation], []);

    private static CSharpCompilation Compilation(params (string Path, string Source)[] files) => CSharpCompilation.Create(
        "Fixture",
        files.Select(static f => CSharpSyntaxTree.ParseText(f.Source, path: f.Path, cancellationToken: TestContext.Current.CancellationToken)),
        RoslynTestCompilations.References,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
