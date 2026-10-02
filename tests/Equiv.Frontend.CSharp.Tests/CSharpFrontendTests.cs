using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests;

public sealed class CSharpFrontendTests
{
    private sealed class StubLoader(Func<string, LoadedSolution> load) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, Codebase side, CancellationToken ct) => Task.FromResult(load(solutionPath));
    }

    private sealed class ThrowingLoader(SolutionLoadException exception) : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, Codebase side, CancellationToken ct) => throw exception;
    }

    /// <summary>Records which side each path was loaded as.</summary>
    private sealed class SideRecordingLoader(Compilation compilation) : ISolutionLoader
    {
        public List<(string Path, Codebase Side)> Loads { get; } = [];

        public Task<LoadedSolution> LoadAsync(string solutionPath, Codebase side, CancellationToken ct)
        {
            Loads.Add((solutionPath, side));
            return Task.FromResult(new LoadedSolution(null!, [compilation], [], []));
        }
    }

    /// <summary>
    /// Ticket P2-085: the loader is told which side it loads, because only a modern project that does not compile is kept
    /// (ADR 0029 as clarified).
    /// </summary>
    [Fact]
    public void Analyze_LoadsEachSolutionAsItsOwnSide()
    {
        SideRecordingLoader loader = new(RoslynTestCompilations.Compile("namespace N { public class C { public int M() => 1; } }", "App"));

        _ = new CSharpFrontend(loader, new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);

        Assert.Equal([("legacy.sln", Codebase.Legacy), ("modern.sln", Codebase.Modern)], loader.Loads);
    }

    [Theory]
    [InlineData("a.sln", true)]
    [InlineData("a.slnx", true)]
    [InlineData("a.SLN", true)]
    [InlineData("a.csproj", false)]
    [InlineData("a.txt", false)]
    public void SupportsOnlySlnAndSlnx(string path, bool expected) =>
        Assert.Equal(expected, new CSharpFrontend(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher()).Supports(path));

    /// <summary>M3-029 acceptance criterion 1: MSBuildWorkspace on Windows, the composite (bare for non-SDK projects) elsewhere.</summary>
    [Fact]
    public void NonWindowsRoutesToTheCompositeLoader()
    {
        Assert.IsType<CompositeSolutionLoader>(CSharpFrontend.CreateLoader(isWindows: false));
        Assert.IsType<MsBuildSolutionLoader>(CSharpFrontend.CreateLoader(isWindows: true));
        Assert.Equal("csharp", new CSharpFrontend().Language);
    }

    [Fact]
    public void SupportsRejectsANullPath() =>
        Assert.Throws<ArgumentNullException>("path", () => new CSharpFrontend(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher()).Supports(null!));

    [Fact]
    public void ASkippedProjectInAnotherLanguageLeavesTheOtherSidesSameNamedAssemblyAdded()
    {
        Compilation shared = RoslynTestCompilations.Compile("namespace S { public class C { public void M() {} } }", "Shared");
        Compilation modernOther = RoslynTestCompilations.Compile("namespace O { public class E { public void Z() {} } }", "Other");
        LoadDiagnostic unsupported = new(LoadDiagnosticKind.UnsupportedProject, string.Empty, "Other", "project language 'Visual Basic' is not supported; only C# is");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [shared], [], [new SkippedProject("Other", "Other", IsCSharp: false, [unsupported], Compilation: null)])
            : new LoadedSolution(null!, [shared, modernOther], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        // ADR 0029 exempts only a skipped C# counterpart: a project that was never C# does not make Z() unverified.
        Assert.Equal(["O.E::Z()"], result.Added.Select(static i => i.Value), StringComparer.Ordinal);
        Assert.Empty(Assert.Single(result.LegacySkipped).Procedures);
    }

    [Fact]
    public void ProjectWithTypesButNoProcedures_IsSkipped()
    {
        // P2-018: a loaded project that declares a type but whose enumeration finds no procedures is a load
        // failure, not an empty project (ADR 0029). Its would-be counterpart on the other side becomes unverified
        // instead of Added, exactly as a project the loader itself skipped does.
        Compilation legacyVacuous = RoslynTestCompilations.Compile("namespace N { public class Empty { public int X; ~Empty() { } } }", "Vacuous");
        Compilation modernVacuous = RoslynTestCompilations.Compile("namespace N { public class Empty { public int X; public void M() {} } }", "Vacuous");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyVacuous], [], [])
            : new LoadedSolution(null!, [modernVacuous], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.Added);
        UnverifiedProject skipped = Assert.Single(result.LegacySkipped);
        Assert.Equal(("Vacuous", "Vacuous", true), (skipped.Name, skipped.AssemblyName, skipped.IsCSharp));
        Assert.Equal(
            ["the project loaded and declares at least one type, but symbol enumeration found zero procedures"],
            skipped.Diagnostics,
            StringComparer.Ordinal);
        Assert.Equal(["N.Empty::M()"], skipped.Procedures.Select(static i => i.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void AVacuousProjectIsSkippedWhateverItsOtherTreesHoldAndItsNeighboursStay()
    {
        // A vacuous project needs only one tree with a type declaration; an attributes-only tree beside it changes
        // nothing. A project with procedures in the same side is kept.
        Compilation vacuous = RoslynTestCompilations.Compile("namespace N { public class Empty { public int X; ~Empty() { } } }", "Vacuous")
            .AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyTitle(\"X\")]", cancellationToken: TestContext.Current.CancellationToken));
        Compilation real = RoslynTestCompilations.Compile("namespace R { public class C { public int M() { return 1; } } }", "Real");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [vacuous, real], [], [])
            : new LoadedSolution(null!, [real], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Equal("Vacuous", Assert.Single(result.LegacySkipped).Name);
        Assert.Equal("R.C::M()", Assert.Single(result.Pairs).Old.Value);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void ProjectWithOnlyAssemblyAttributes_IsNotSkipped()
    {
        // P2-018 acceptance criterion 3: a project with no type declaration at all was never going to yield
        // procedures, so it is loaded normally rather than treated as a vacuous-side failure.
        Compilation legacy = RoslynTestCompilations.Compile("[assembly: System.Reflection.AssemblyTitle(\"X\")]", "AttributesOnly");
        Compilation modern = RoslynTestCompilations.Compile("[assembly: System.Reflection.AssemblyTitle(\"X\")]", "AttributesOnly");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacy], [], [])
            : new LoadedSolution(null!, [modern], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.LegacySkipped);
        Assert.Empty(result.ModernSkipped);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void ProjectWhoseOnlyTypeIsEmpty_IsNotSkipped()
    {
        // P2-084 acceptance criterion 1: a placeholder project (one empty class, there so the build accepts the
        // project's content files) has no method to lose, so it loads and nothing is reported.
        Compilation placeholder = RoslynTestCompilations.Compile("namespace N { public class Placeholder { } }", "Placeholder");
        StubLoader loader = new(_ => new LoadedSolution(null!, [placeholder], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.LegacySkipped);
        Assert.Empty(result.ModernSkipped);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Theory]
    [InlineData("public class C { public int X; }", false)]
    [InlineData("public enum E { A }", false)]
    [InlineData("public interface I { void M(); }", false)]
    [InlineData("public abstract class C { public abstract void M(); }", false)]
    [InlineData("int F() => 1; System.Console.Write(F());", false)]
    [InlineData("public class C { ~C() { } }", true)]
    [InlineData("public class C { ~C() => System.Console.Write(1); }", true)]
    [InlineData("public class C { public event System.Action E { add { } remove { } } }", true)]
    [InlineData("public interface I { int P { get; } }", true)]
    public void AProjectIsVacuousOnlyWhenATypeDeclaresABodyAndNoProcedureIsFound(string source, bool vacuous)
    {
        // P2-084: with zero procedures enumerated, the project is a load failure only when a type's syntax holds a
        // member with a body, an expression body or an accessor. A body outside any type (top-level statements)
        // does not count, as before.
        Compilation compilation = RoslynTestCompilations.Compile(source, "Candidate");
        StubLoader loader = new(_ => new LoadedSolution(null!, [compilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Equal(vacuous ? ["Candidate"] : [], result.LegacySkipped.Select(static p => p.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void NoProcedures_ExitsFour()
    {
        // CompareCommand exits 4 for any UnverifiedProject with IsCSharp: true (ExitCodePrecedenceIsFourThenVerdicts,
        // LowerOnlyExits4WhenACSharpProjectWasSkipped, Equiv.Cli.Tests). This proves the frontend's contribution to
        // that contract: a vacuous project is reported with IsCSharp: true, not merely as a warning.
        Compilation legacyVacuous = RoslynTestCompilations.Compile("namespace N { public class Empty { public int X; ~Empty() { } } }", "Vacuous");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyVacuous], [], [])
            : new LoadedSolution(null!, [], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.True(Assert.Single(result.LegacySkipped).IsCSharp);
    }

    [Fact]
    public void PublicConstructorWiresProductionCollaborators() =>
        Assert.Equal("csharp", new CSharpFrontend().Language);

    [Fact]
    public void LanguageIsCsharp() =>
        Assert.Equal("csharp", new CSharpFrontend(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher()).Language);

    [Fact]
    public void AppliesRenameMapBeforeMatching()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace Old.Ns { public class C { public void M() {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace New.Ns { public class C { public void M() {} } }");

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "New.Ns") };
        EquivConfig config = EquivConfig.Default with { Renames = renames };

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", config, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Single(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void PassesEachSidesProjectsNotBuiltThrough()
    {
        Compilation compilation = RoslynTestCompilations.Compile("namespace N { public class C { public void M() {} } }");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [compilation], [], []) { NotBuilt = ["Example.Site", "_build"] }
            : new LoadedSolution(null!, [compilation], [], []));

        FrontendAnalysis analysis = new CSharpFrontend(loader, new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);

        Assert.Equal(["Example.Site", "_build"], analysis.LegacyNotBuilt);
        Assert.Empty(analysis.ModernNotBuilt);
    }

    /// <summary>
    /// Ticket M3-009 acceptance criterion 4: only the legacy body is rewritten, and the pair lists what fired. The modern
    /// body is given no entry, so its call to the legacy member stays that member's (<c>Kept</c>).
    /// </summary>
    [Fact]
    public void RecordsTheEquivalencesAppliedToTheLegacyBody()
    {
        const string Kept = "public bool Kept(string s, char c) { return System.Linq.Enumerable.Contains(s, c); }";
        Compilation legacyCompilation = RoslynTestCompilations.Compile($"namespace N {{ public class C {{ public bool M(string s, char c) => System.Linq.Enumerable.Contains(s, c); {Kept} }} }}");
        Compilation modernCompilation = RoslynTestCompilations.Compile($"namespace N {{ public class C {{ public bool M(string s, char c) => s.Contains(c); {Kept} }} }}");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair pair = result.Pairs.Single(static p => p.New.Value.Contains("::M(", StringComparison.Ordinal));
        Assert.Equal(["bcl.string-contains-char"], pair.EquivalencesApplied);
        Assert.Contains("System.String::Contains(char)", IrText.Dump(pair.OldBody!), StringComparison.Ordinal);
        Assert.Empty(pair.ReboundCalls);

        // ADR 0042: the catalogue is applied before call sites are compared. A modern side that still calls the legacy
        // member binds the same text to another identity than the rewritten legacy call, so that site is rebound.
        ProcedurePair kept = result.Pairs.Single(static p => p.New.Value.Contains("::Kept(", StringComparison.Ordinal));
        Assert.Equal(["bcl.string-contains-char"], kept.EquivalencesApplied);
        Assert.Equal("System.String::Contains(char)", Assert.Single(kept.ReboundCalls).Legacy);
        Assert.StartsWith("System.Linq.Enumerable::Contains", Assert.Single(kept.ReboundCalls).Modern, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ticket P1-016: under <c>--il-fallback</c> a pair that is not congruent and holds an unshared opaque keeps the IL bodies
    /// when they hold fewer, and every pair says which lowering it kept; a congruent pair keeps its IOperation bodies and the
    /// equivalences they applied. Without the flag no pair names a lowering.
    /// </summary>
    [Fact]
    public void UnderTheIlFallbackEveryPairNamesItsLowering()
    {
        const string Contains = "public bool Has(string s, char c) => System.Linq.Enumerable.Contains(s, c);";
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            $"namespace N {{ public class C {{ {Contains} public int? Add(int? a, int b) {{ if (a.HasValue) {{ return new int?(a.GetValueOrDefault() + b); }} return null; }} }} }}");
        Compilation modernCompilation = RoslynTestCompilations.Compile($"namespace N {{ public class C {{ {Contains} public int? Add(int? a, int b) => a + b; }} }}");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));
        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());

        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { IlFallback = true }, NullRunLog.Instance, CancellationToken.None).Match;
        MatchResult plain = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair add = result.Pairs.Single(static p => p.New.Value.Contains("::Add(", StringComparison.Ordinal));
        ProcedurePair has = result.Pairs.Single(static p => p.New.Value.Contains("::Has(", StringComparison.Ordinal));
        Assert.Equal(("il", true), (add.Lowering, add.IlFallbackTried));
        Assert.DoesNotContain(add.NewBody!.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
        Assert.Equal(("operation", false), (has.Lowering, has.IlFallbackTried));
        Assert.Equal(["bcl.string-contains-char"], has.EquivalencesApplied);
        Assert.All(plain.Pairs, static p => Assert.Equal((null, false), (p.Lowering, p.IlFallbackTried)));
    }

    /// <summary>
    /// ADR 0042 (ticket P2-069 criteria 2 and 3): a call site with the same text that binds to another callee is listed as a
    /// rebound pair and is an opaque in both bodies, each lowered a second time for it. A call to another member is an
    /// ordinary call in a pair lowered once.
    /// </summary>
    [Fact]
    public void AReboundCallSiteIsListedAndOpaqueInBothBodies()
    {
        Dictionary<string, int> lowerings = new(StringComparer.Ordinal);
        CSharpFrontend frontend = new(ReboundLoader(), new StableIdentityMatcher(), (symbol, compilation, config, legacy, sites) =>
        {
            lowerings[symbol.Name] = lowerings.GetValueOrDefault(symbol.Name) + 1;
            return CSharpFrontend.LowerWithIrLowerer(symbol, compilation, config, legacy, sites);
        });

        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair has = result.Pairs.Single(static p => p.New.Value.Contains("::Has(", StringComparison.Ordinal));
        Assert.Equal([new ReboundCall(LegacyExists, ModernExists)], has.ReboundCalls);
        foreach (IrProcedure body in (IrProcedure[])[has.OldBody!, has.NewBody!])
        {
            Assert.Equal(ReboundCall.OpaqueReason, Assert.Single(body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()).Reason);
            Assert.Equal(["Lib.IFs::get_File()"], body.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static c => c.Callee.Value), StringComparer.Ordinal);
        }

        ProcedurePair clear = result.Pairs.Single(static p => p.New.Value.Contains("::Clear(", StringComparison.Ordinal));
        Assert.Empty(clear.ReboundCalls);
        Assert.Contains(LegacyExists, IrText.Dump(clear.OldBody!), StringComparison.Ordinal);
        Assert.Contains("Lib.IFile::Delete(string)", IrText.Dump(clear.NewBody!), StringComparison.Ordinal);
        Assert.DoesNotContain(clear.NewBody!.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
        Assert.Equal(4, lowerings["Has"]);
        Assert.Equal(2, lowerings["Clear"]);
    }

    /// <summary>ADR 0042: a legacy identity the config's call-identity map renames to the modern one is the same call, so it stays a call.</summary>
    [Fact]
    public void ACallIdentityRenameKeepsAReboundSiteACall()
    {
        EquivConfig config = EquivConfig.Default with { CallIdentityRenames = EquivConfig.Default.CallIdentityRenames.Add(LegacyExists, ModernExists) };

        MatchResult result = new CSharpFrontend(ReboundLoader(), new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", config, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair has = result.Pairs.Single(static p => p.New.Value.Contains("::Has(", StringComparison.Ordinal));
        Assert.Empty(has.ReboundCalls);
        Assert.Contains(LegacyExists, IrText.Dump(has.OldBody!), StringComparison.Ordinal);
        Assert.Contains(ModernExists, IrText.Dump(has.NewBody!), StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0042: under <c>--il-fallback</c> the IL lowering is given each side's rebound identities, so it holds the same
    /// opaques and the pair keeps its IOperation bodies. Read without them, the IL bodies would hold none and be preferred.
    /// </summary>
    [Fact]
    public void UnderTheIlFallbackAReboundCallIsOpaqueInTheIlBodiesToo()
    {
        StubLoader loader = ReboundLoader(out Compilation legacy, out Compilation modern);

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { IlFallback = true }, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair has = result.Pairs.Single(static p => p.New.Value.Contains("::Has(", StringComparison.Ordinal));
        Assert.Equal(("operation", true), (has.Lowering, has.IlFallbackTried));
        Assert.Equal([new ReboundCall(LegacyExists, ModernExists)], has.ReboundCalls);
        foreach ((Compilation compilation, string identity) in new[] { (legacy, LegacyExists), (modern, ModernExists) })
        {
            IMethodSymbol method = compilation.GetTypeByMetadataName("N.Probe")!.GetMembers("Has").OfType<IMethodSymbol>().Single();
            Assert.DoesNotContain(IlLowerer.Lower(method, compilation).Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
            Assert.Equal(
                ReboundCall.OpaqueReason,
                Assert.Single(IlLowerer.Lower(method, compilation, rebound: [identity]).Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()).Reason);
        }
    }

    private const string LegacyExists = "Lib.FileBase::Exists(string)";

    private const string ModernExists = "Lib.IFile::Exists(string)";

    private static StubLoader ReboundLoader() => ReboundLoader(out _, out _);

    /// <summary>
    /// P2-069's pair: a library whose <c>IFs.File</c> is a class on the legacy side and an interface on the modern side, <c>Has</c>
    /// with the same source on both sides, and <c>Clear</c>, which calls another member on the modern side.
    /// </summary>
    private static StubLoader ReboundLoader(out Compilation legacy, out Compilation modern)
    {
        const string Has = "public static bool Has(Lib.IFs fs, string p) => fs.File.Exists(p);";
        Compilation legacyCompilation = legacy = RoslynTestCompilations.Compile(
            "namespace Lib { public abstract class FileBase { public abstract bool Exists(string p); public abstract bool Delete(string p); } public interface IFs { FileBase File { get; } } }"
            + $"namespace N {{ public static class Probe {{ {Has} public static bool Clear(Lib.IFs fs, string p) => fs.File.Exists(p); }} }}");
        Compilation modernCompilation = modern = RoslynTestCompilations.Compile(
            "namespace Lib { public interface IFile { bool Exists(string p); bool Delete(string p); } public interface IFs { IFile File { get; } } }"
            + $"namespace N {{ public static class Probe {{ {Has} public static bool Clear(Lib.IFs fs, string p) => fs.File.Delete(p); }} }}");
        return new StubLoader(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));
    }

    /// <summary>Ticket M3-015 acceptance criterion 1: every lowered pair carries both bodies' fingerprints.</summary>
    [Fact]
    public void FingerprintsBothBodiesOfEveryLoweredPair()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public int Same(int a) => a + 1; public int Changed(int a) => a + 1; } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public int Same(int b) =>  b+1; public int Changed(int a) => a + 2; } }");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair same = result.Pairs.Single(static p => p.New.Value.Contains("::Same(", StringComparison.Ordinal));
        ProcedurePair changed = result.Pairs.Single(static p => p.New.Value.Contains("::Changed(", StringComparison.Ordinal));
        Assert.NotNull(same.OldFingerprint);
        Assert.Equal(same.OldFingerprint, same.NewFingerprint);
        Assert.NotEqual(changed.OldFingerprint, changed.NewFingerprint);
    }

    /// <summary>
    /// Ticket M4-006 acceptance criterion 2: when exactly one side is <c>async</c>, both bodies are one whole-body opaque
    /// with reason <c>async-mismatch</c> at the method's name, keeping its signature; when both are, neither is.
    /// </summary>
    [Fact]
    public void AnAsyncMismatchMakesBothBodiesOneOpaque()
    {
        const string Task = "System.Threading.Tasks.Task";
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            $"namespace N {{ public class C {{ public {Task} Sync({Task} t) => t; public async {Task} Both({Task} t) {{ await t; }} }} }}");
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            $"namespace N {{ public class C {{ public async {Task} Sync({Task} t) {{ await t; }} public async {Task} Both({Task} t) {{ await t; }} }} }}");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher()).Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair mismatched = result.Pairs.Single(static p => p.New.Value.Contains("::Sync(", StringComparison.Ordinal));
        foreach (IrProcedure body in (IrProcedure[])[mismatched.OldBody!, mismatched.NewBody!])
        {
            IrOpaque opaque = Assert.IsType<IrOpaque>(Assert.Single(Assert.Single(body.Blocks).Instructions));
            Assert.Equal("async-mismatch", opaque.Reason);
            Assert.True(opaque.WholeBody);
            Assert.Equal(opaque.Span.StartColumn + "Sync".Length, opaque.Span.EndColumn);
            Assert.Empty(IrValidator.Validate(body));
        }

        Assert.Equal("t", mismatched.NewBody!.Parameters[0].Var.Name);
        ProcedurePair both = result.Pairs.Single(static p => p.New.Value.Contains("::Both(", StringComparison.Ordinal));
        Assert.DoesNotContain(both.OldBody!.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
    }

    [Fact]
    public void LowersBothBodiesOfEveryMatchedPair()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace Old.Ns { public class C { public int M(int a) => a + 1; public void F(int a) {} public void F(long a) {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace New.Ns { public class C { public int M(int a) => a + 2; } }");

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "New.Ns") };
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { Renames = renames }, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal(pair.Old, pair.OldBody!.Identity);
        Assert.Equal(pair.New, pair.NewBody!.Identity);
        Assert.Empty(IrValidator.Validate(pair.OldBody));
        Assert.Empty(IrValidator.Validate(pair.NewBody));
        Assert.NotEqual(pair.OldBody, pair.NewBody);
    }

    [Fact]
    public void ProducesAddedAndRemovedWithLocations()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public void OnlyLegacy() {} } }");
        Compilation modernCompilation = RoslynTestCompilations.Compile("namespace N { public class C { public void OnlyModern() {} } }");

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());
        MatchResult result = frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedureIdentity removed = Assert.Single(result.Removed);
        ProcedureIdentity added = Assert.Single(result.Added);
        Assert.NotNull(removed.Location);
        Assert.NotNull(added.Location);
        Assert.Equal("N.C::OnlyLegacy()", removed.Value);
        Assert.Equal("N.C::OnlyModern()", added.Value);
    }

    [Fact]
    public void WrapsLoaderFailure()
    {
        SolutionLoadException loadException = new("legacy.sln", []);
        ThrowingLoader loader = new(loadException);
        CSharpFrontend frontend = new(loader, new StableIdentityMatcher());

        FrontendLoadException exception = Assert.Throws<FrontendLoadException>(
            () => frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None));

        Assert.Equal("legacy.sln", exception.Path);
    }

    /// <summary>P2-011: a lowering fault in one of two pairs costs only that pair; the other is still lowered.</summary>
    [Fact]
    public void Analyze_LoweringFaultInOnePair_RecordsItAndLowersTheOther()
    {
        const string Source = "namespace N { public class C { public int Good(int a) => a + 1; public int Bad(int a) => a - 1; } }";
        StubLoader loader = new(_ => new LoadedSolution(null!, [RoslynTestCompilations.Compile(Source)], [], []));
        InvalidOperationException fault = new("injected lowering fault");

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher(), FaultOn("Bad", fault))
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal("N.C::Good(int)", pair.New.Value);
        Assert.NotNull(pair.OldBody);
        Assert.NotNull(pair.NewBody);
        LoweringFailure failure = Assert.Single(result.LoweringFailures);
        Assert.Equal("N.C::Bad(int)", failure.Old.Value);
        Assert.Equal("N.C::Bad(int)", failure.New.Value);
        Assert.Same(fault, failure.Exception);
    }

    /// <summary>P2-011, as ADR 0023 does for verification: cancellation and out-of-memory are not a pair's fault.</summary>
    [Fact]
    public void Analyze_LoweringCancelled_Propagates()
    {
        StubLoader loader = new(_ => new LoadedSolution(null!, [RoslynTestCompilations.Compile("namespace N { public class C { public int Bad(int a) => a; } }")], [], []));
        CSharpFrontend frontend = new(loader, new StableIdentityMatcher(), FaultOn("Bad", new OperationCanceledException()));

        Assert.Throws<OperationCanceledException>(() => frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None));
    }

    /// <summary>CA2201 reserves <see cref="OutOfMemoryException"/> for the runtime; <see cref="InsufficientMemoryException"/> is its BCL subclass.</summary>
    [Fact]
    public void Analyze_LoweringOutOfMemory_Propagates()
    {
        StubLoader loader = new(_ => new LoadedSolution(null!, [RoslynTestCompilations.Compile("namespace N { public class C { public int Bad(int a) => a; } }")], [], []));
        CSharpFrontend frontend = new(loader, new StableIdentityMatcher(), FaultOn("Bad", new InsufficientMemoryException()));

        Assert.Throws<InsufficientMemoryException>(() => frontend.Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None));
    }

    /// <summary>The production lowering, except that a method named <paramref name="name"/> throws <paramref name="fault"/>.</summary>
    private static Func<IMethodSymbol, Compilation, EquivConfig, bool, CallSites, (IrProcedure, ImmutableArray<string>)> FaultOn(string name, Exception fault) =>
        (symbol, compilation, config, legacy, sites) => string.Equals(symbol.Name, name, StringComparison.Ordinal)
            ? throw fault
            : CSharpFrontend.LowerWithIrLowerer(symbol, compilation, config, legacy, sites);

    [Fact]
    public void NullConfigThrows()
    {
        CSharpFrontend frontend = new(new StubLoader(_ => throw new InvalidOperationException()), new StableIdentityMatcher());
        Assert.Throws<ArgumentNullException>(() => frontend.Analyze("a.sln", "b.sln", null!, NullRunLog.Instance, CancellationToken.None));
    }

    // Declared as a separate referenced assembly, not inlined into the compilation under test: an
    // inlined fake attribute class's own (explicit) constructor would otherwise show up as just another
    // procedure for ProcedureEnumerator/CSharpFrontend.Analyze to enumerate and match.
    private static readonly MetadataReference LegacyRouteAttributes = RoslynTestCompilations.Compile(
        """
        namespace System.Web.Http
        {
            public class RouteAttribute : System.Attribute { public RouteAttribute(string template = null) { } }
            public class HttpGetAttribute : System.Attribute { }
            public class HttpPostAttribute : System.Attribute { }
        }
        """,
        "LegacyRouteAttributes").ToReference();

    private static readonly MetadataReference ModernRouteAttributes = RoslynTestCompilations.Compile(
        """
        namespace Microsoft.AspNetCore.Mvc
        {
            public class RouteAttribute : System.Attribute { public RouteAttribute(string template = null) { } }
            public class HttpGetAttribute : System.Attribute { public HttpGetAttribute(string template = null) { } }
            public class HttpPostAttribute : System.Attribute { public HttpPostAttribute(string template = null) { } }
        }
        """,
        "ModernRouteAttributes").ToReference();

    /// <summary>
    /// M2-005 acceptance criterion 3: an endpoint match bridges legacy/modern actions whose plain C#
    /// identities differ (different namespaces here, with no user rename map at all), and it becomes
    /// the pair's identity (both criterion 3's "MatchResult gains nothing" and criterion 4's SARIF
    /// <c>fullyQualifiedName</c> need the pair's own <see cref="ProcedureIdentity.Value"/> to already be
    /// <c>"VERB /template"</c>). A route with no counterpart at all on the other side — legacy-only
    /// (<c>Old.Ns.OrdersController.Delete</c>) and modern-only (<c>New.Ns.OrdersController.Create</c>) —
    /// is unaffected: both fall through to ordinary Removed/Added handling instead of ever reaching the
    /// endpoint rename map.
    /// </summary>
    [Fact]
    public void Analyze_EndpointRenameMapBeforeUserMap()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace Old.Ns
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpPost, Route("api/orders/gone")]
                    public void Delete() { }
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace New.Ns
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpPost("api/orders/new")]
                    public void Create() { }
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal("GET /api/orders/{id}", pair.Old.Value);
        Assert.Equal("GET /api/orders/{id}", pair.New.Value);
        ProcedureIdentity removed = Assert.Single(result.Removed);
        Assert.Contains("Delete", removed.Value, StringComparison.Ordinal);
        ProcedureIdentity added = Assert.Single(result.Added);
        Assert.Contains("Create", added.Value, StringComparison.Ordinal);
        Assert.Empty(result.Ambiguous);
    }

    /// <summary>
    /// M2-005 acceptance criterion 3: "ahead of the user's rename map (user entries win on conflict)".
    /// The legacy action's namespace is explicitly renamed by the user to a namespace that is NOT where
    /// the modern action actually lives, so the config rename map changes this action's own identity;
    /// the endpoint rename map must not override that explicit choice by silently pairing it with the
    /// modern action via the route match instead.
    /// </summary>
    [Fact]
    public void UserRenameMapWinsOverEndpointRenameOnConflict()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace Old.Ns
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace Other.Ns
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        RenameMap renames = RenameMap.Empty with { Namespaces = RenameMap.Empty.Namespaces.Add("Old.Ns", "Different.Ns") };
        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default with { Renames = renames }, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Ambiguous);
        Assert.Single(result.Added);
        Assert.Single(result.Removed);
    }

    /// <summary>
    /// M2-005 acceptance criterion 3: a (Verb, Template) with duplicates on one side but present on both
    /// is ignored for the rename map and every action sharing it lands in <see cref="MatchResult.Ambiguous"/>
    /// instead (SARIF Unknown(UnmatchedOverload) wiring is M3-003).
    /// </summary>
    [Fact]
    public void Analyze_DuplicateEndpointYieldsUnknown()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpGet, Route("api/orders/{id}")]
                    public int GetOrder(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(3, result.Ambiguous.Length);
    }

    /// <summary>The mirror of <see cref="Analyze_DuplicateEndpointYieldsUnknown"/>: duplicates on the modern side this time.</summary>
    [Fact]
    public void DuplicateEndpointOnTheModernSideYieldsAmbiguous()
    {
        Compilation legacyCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """,
            [LegacyRouteAttributes]);
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int Get(int id) => id;

                    [HttpGet("api/orders/{id}")]
                    public int GetOrder(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes]);

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [legacyCompilation], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(3, result.Ambiguous.Length);
    }

    [Fact]
    public void SkippedProjectProceduresAreUnverifiedNotAddedOrRemoved()
    {
        Compilation shared = RoslynTestCompilations.Compile("namespace S { public class C { public void M() {} } }", "Shared");
        Compilation legacyLib = RoslynTestCompilations.Compile("namespace L { public class D { public void X() {} Missing f; } }", "Lib");
        Compilation modernLib = RoslynTestCompilations.Compile("namespace L { public class D { public void X() {} public void Y() {} } }", "Lib");
        Compilation modernOther = RoslynTestCompilations.Compile("namespace O { public class E { public void Z() {} } }", "Other");
        Compilation legacyTool = RoslynTestCompilations.Compile("namespace T { public class F { public void W() {} } }", "Tool");
        LoadDiagnostic unresolved = new(LoadDiagnosticKind.UnresolvedReference, "CS0246", "Lib", "The type or namespace name 'Missing' could not be found");
        LoadDiagnostic workspace = new(LoadDiagnosticKind.UnsupportedProject, string.Empty, "Native", "Cannot open project 'Native.vcxproj'");

        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [shared, legacyTool], [], [
                new SkippedProject("Lib", "Lib", IsCSharp: true, [unresolved], legacyLib),
                new SkippedProject("Native", "Native", IsCSharp: false, [workspace], Compilation: null)])
            : new LoadedSolution(null!, [shared, modernLib, modernOther], [], [
                new SkippedProject("Tool", "Tool", IsCSharp: true, [], Compilation: null)]));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Equal(["S.C::M()"], result.Pairs.Select(static p => p.New.Value), StringComparer.Ordinal);

        // O.E::Z() is in an assembly no skipped project names, so it is still Added.
        Assert.Equal(["O.E::Z()"], result.Added.Select(static i => i.Value), StringComparer.Ordinal);
        Assert.Empty(result.Removed);

        Assert.Equal(2, result.LegacySkipped.Length);
        UnverifiedProject lib = result.LegacySkipped[0];
        Assert.Equal(("Lib", "Lib", true), (lib.Name, lib.AssemblyName, lib.IsCSharp));
        Assert.Equal(["CS0246: The type or namespace name 'Missing' could not be found"], lib.Diagnostics, StringComparer.Ordinal);
        Assert.Equal(["L.D::X()", "L.D::Y()"], lib.Procedures.Select(static i => i.Value), StringComparer.Ordinal);

        UnverifiedProject native = result.LegacySkipped[1];
        Assert.False(native.IsCSharp);
        Assert.Equal(["Cannot open project 'Native.vcxproj'"], native.Diagnostics, StringComparer.Ordinal);
        Assert.Empty(native.Procedures);

        UnverifiedProject tool = Assert.Single(result.ModernSkipped);
        Assert.Equal(["T.F::W()"], tool.Procedures.Select(static i => i.Value), StringComparer.Ordinal);
    }

    /// <summary>
    /// P2-016: a multi-targeted project loads once per target framework, all under one assembly name. Each declaration is
    /// one procedure, taken from the last flavour that declares it, so it pairs instead of going Ambiguous; a
    /// declaration only one flavour compiles (an <c>#if</c>) is still that side's own.
    /// </summary>
    [Fact]
    public void Analyze_TargetFrameworkFlavoursOfOneProjectAreOneProcedure()
    {
        Compilation net20 = RoslynTestCompilations.Compile("namespace N { public class C { public int M(int a) => a; public void OnlyNet20() {} } }", "Lib");
        Compilation net40 = RoslynTestCompilations.Compile("namespace N { public class C { public int M(int a) => a + 0; } }", "Lib");
        Compilation modern = RoslynTestCompilations.Compile("namespace N { public class C { public int M(int a) => a; } }", "Lib");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [net20, net40], [], [])
            : new LoadedSolution(null!, [modern], [], []));
        List<Compilation> lowered = [];

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher(), (symbol, compilation, config, legacy, sites) =>
            {
                lowered.Add(compilation);
                return CSharpFrontend.LowerWithIrLowerer(symbol, compilation, config, legacy, sites);
            })
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Equal(["N.C::M(int)"], result.Pairs.Select(static p => p.New.Value), StringComparer.Ordinal);
        Assert.Equal(["N.C::OnlyNet20()"], result.Removed.Select(static i => i.Value), StringComparer.Ordinal);
        Assert.Empty(result.Ambiguous);
        Assert.Equal([net40, modern], lowered);
    }

    /// <summary>P2-016 collapses flavours, not assemblies: one file linked into two projects is still Ambiguous.</summary>
    [Fact]
    public void Analyze_OneDeclarationInTwoAssembliesStaysAmbiguous()
    {
        const string Source = "namespace N { public class C { public void M() {} } }";
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [RoslynTestCompilations.Compile(Source, "A")], [], [])
            : new LoadedSolution(null!, [RoslynTestCompilations.Compile(Source, "A"), RoslynTestCompilations.Compile(Source, "B")], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Empty(result.Pairs);
        Assert.Equal(["N.C::M()"], result.Ambiguous.Select(static i => i.Value), StringComparer.Ordinal);
    }

    /// <summary>P2-016: an action a multi-targeted project compiles once per flavour is one endpoint, not a duplicate route.</summary>
    [Fact]
    public void Analyze_TargetFrameworkFlavoursOfOneControllerAreOneEndpoint()
    {
        const string Legacy = """
            namespace N
            {
                using System.Web.Http;

                public class OrdersController
                {
                    [HttpGet, Route("api/orders/{id}")]
                    public int Get(int id) => id;
                }
            }
            """;
        Compilation modernCompilation = RoslynTestCompilations.Compile(
            """
            namespace N
            {
                using Microsoft.AspNetCore.Mvc;

                public class OrdersController
                {
                    [HttpGet("api/orders/{id}")]
                    public int GetOrder(int id) => id;
                }
            }
            """,
            [ModernRouteAttributes],
            "Web");
        StubLoader loader = new(path => string.Equals(path, "legacy.sln", StringComparison.Ordinal)
            ? new LoadedSolution(null!, [RoslynTestCompilations.Compile(Legacy, [LegacyRouteAttributes], "Web"), RoslynTestCompilations.Compile(Legacy, [LegacyRouteAttributes], "Web")], [], [])
            : new LoadedSolution(null!, [modernCompilation], [], []));

        MatchResult result = new CSharpFrontend(loader, new StableIdentityMatcher())
            .Analyze("legacy.sln", "modern.sln", EquivConfig.Default, NullRunLog.Instance, CancellationToken.None).Match;

        Assert.Equal(["GET /api/orders/{id}"], result.Pairs.Select(static p => p.New.Value), StringComparer.Ordinal);
        Assert.Empty(result.Ambiguous);
    }
}
