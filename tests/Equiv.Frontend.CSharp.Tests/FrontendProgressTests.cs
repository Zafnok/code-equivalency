using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp.Loading;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

/// <summary>Ticket M4-013: the phases <see cref="CSharpFrontend.Analyze"/> reports to the run log (ADR 0038).</summary>
public sealed class FrontendProgressTests
{
    private sealed class StubLoader(Func<string, LoadedSolution> load) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) =>
            string.Equals(solutionPath, "bad.sln", StringComparison.Ordinal) ? throw new SolutionLoadException(solutionPath, []) : Task.FromResult(load(solutionPath));
    }

    private static CSharpFrontend Frontend(bool skipOnLegacy = false)
    {
        Compilation legacy = RoslynTestCompilations.Compile("namespace N { public class C { public int A() => 1; public int B() => 2; } }", "App");
        Compilation modern = RoslynTestCompilations.Compile("namespace N { public class C { public int A() => 1; public int B() => 2; } }", "App");
        LoadDiagnostic diagnostic = new(LoadDiagnosticKind.UnresolvedReference, "CS0246", "Broken", "type Foo not found");
        LoadDiagnostic bare = new(LoadDiagnosticKind.UnsupportedProject, string.Empty, "Native", "project language 'C++' is not supported");
        ImmutableArray<SkippedProject> skipped = skipOnLegacy
            ? [new SkippedProject("Broken", "Broken", IsCSharp: true, [diagnostic], legacy), new SkippedProject("Native", "Native", IsCSharp: false, [bare], Compilation: null)]
            : [];
        return new CSharpFrontend(
            new StubLoader(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
                ? new LoadedSolution(null!, [legacy], [], skipped)
                : new LoadedSolution(null!, [modern], [], [])),
            new StableIdentityMatcher());
    }

    private static readonly string[] Body =
    [
        "phase load-modern 1 1", "item App 1", "done loaded", "phase-done",
        "phase enumerate 2 2", "item legacy 1", "done 2 procedures", "item modern 1", "done 2 procedures", "phase-done",
        "phase match 1 4", "item procedures 4", "done 2 pairs", "phase-done",
        "phase lower 2 2", "item N.C::A() 1", "done lowered", "item N.C::B() 1", "done lowered", "phase-done",
    ];

    [Fact]
    public void Events_Are_Exact_Without_Debug()
    {
        RecordingRunLog log = new();

        _ = Frontend(skipOnLegacy: true).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        Assert.Equal(
            ["phase load-legacy 3 3", "item App 1", "done loaded", "item Broken 1", "done skipped", "item Native 1", "done skipped", "phase-done", .. Body],
            log.Events,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Events_Are_Exact_With_Debug()
    {
        RecordingRunLog log = new(isDebug: true);

        _ = Frontend(skipOnLegacy: true).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        Assert.Equal(
            [
                "phase load-legacy 3 3", "item App 1", "done loaded",
                "item Broken 1", "done skipped", "detail legacy project Broken skipped: CS0246: type Foo not found",
                "item Native 1", "done skipped", "detail legacy project Native skipped: project language 'C++' is not supported",
                "phase-done",
                .. Body,
            ],
            log.Events,
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_Project_Without_Documents_Weighs_One()
    {
        RecordingRunLog log = new();
        Compilation empty = CSharpCompilation.Create("Empty");
        CSharpFrontend frontend = new(new StubLoader(_ => new LoadedSolution(null!, [empty], [], [])), new StableIdentityMatcher());

        _ = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        Assert.Equal(["phase load-legacy 1 1", "item Empty 1", "done loaded", "phase-done"], log.Events.Take(4), StringComparer.Ordinal);
    }

    [Fact]
    public void Emits_Phases_In_Order()
    {
        RecordingRunLog log = new();

        _ = Frontend().Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        Assert.Equal(
            ["load-legacy", "load-modern", "enumerate", "match", "lower"],
            log.Events.Where(static e => e.StartsWith("phase ", StringComparison.Ordinal)).Select(static e => e.Split(' ')[1]),
            StringComparer.Ordinal);
        Assert.Equal(5, log.Events.Count(static e => string.Equals(e, "phase-done", StringComparison.Ordinal)));
        Assert.Contains("item N.C::A() 1", log.Events, StringComparer.Ordinal);
        Assert.Contains("done lowered", log.Events, StringComparer.Ordinal);
    }

    [Fact]
    public void Totals_Match_Items()
    {
        RecordingRunLog log = new();

        _ = Frontend(skipOnLegacy: true).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        int total = 0;
        int items = 0;
        foreach (string entry in log.Events)
        {
            if (entry.StartsWith("phase ", StringComparison.Ordinal))
            {
                total = int.Parse(entry.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
                items = 0;
            }
            else if (entry.StartsWith("item ", StringComparison.Ordinal))
            {
                items++;
            }
            else if (string.Equals(entry, "phase-done", StringComparison.Ordinal))
            {
                Assert.Equal(total, items);
            }
        }

        Assert.Contains("phase load-legacy 3 3", log.Events, StringComparer.Ordinal);
    }

    [Fact]
    public void Skipped_Project_Is_A_Debug_Detail()
    {
        RecordingRunLog debug = new(isDebug: true);
        RecordingRunLog normal = new();

        _ = Frontend(skipOnLegacy: true).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, debug, CancellationToken.None);
        _ = Frontend(skipOnLegacy: true).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, normal, CancellationToken.None);

        Assert.Contains("detail legacy project Broken skipped: CS0246: type Foo not found", debug.Events, StringComparer.Ordinal);
        Assert.Contains("detail legacy project Native skipped: project language 'C++' is not supported", debug.Events, StringComparer.Ordinal);
        Assert.DoesNotContain(normal.Events, static e => e.StartsWith("detail ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Failed_Load_Is_One_Failed_Item_And_Rethrown()
    {
        RecordingRunLog log = new();

        Assert.Throws<FrontendLoadException>(() => Frontend().Analyze("legacy.sln", "bad.sln", EquivConfig.Default, log, CancellationToken.None));

        Assert.Equal(
            ["phase load-legacy 1 1", "item App 1", "done loaded", "phase-done", "phase load-modern 1 1", "item bad.sln 1", "done failed", "phase-done"],
            log.Events,
            StringComparer.Ordinal);
    }

    [Fact]
    public void A_Failed_Lowering_Is_A_Failed_Item()
    {
        RecordingRunLog log = new();
        CSharpFrontend frontend = new(
            new StubLoader(_ => new LoadedSolution(null!, [RoslynTestCompilations.Compile("namespace N { public class C { public int A() => 1; } }", "App")], [], [])),
            new StableIdentityMatcher(),
            static (_, _, _, _, _, _) => throw new InvalidOperationException("boom"));

        _ = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, log, CancellationToken.None);

        Assert.Contains("done failed", log.Events, StringComparer.Ordinal);
    }

    [Fact]
    public void A_Null_Log_Is_Rejected() =>
        Assert.Throws<ArgumentNullException>("log", () => Frontend().Analyze("legacy.sln", "modern.sln", EquivConfig.Default, null!, CancellationToken.None));
}
