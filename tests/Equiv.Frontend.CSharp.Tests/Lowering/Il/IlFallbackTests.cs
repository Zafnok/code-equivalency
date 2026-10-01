using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>Ticket P1-016: ADR 0039's per-pair rule, which lowering a matched pair keeps under <c>--il-fallback</c>.</summary>
public sealed class IlFallbackTests
{
    /// <summary>The legacy side spells out what the modern side's lifted <c>+</c> compiles to; only the modern side is opaque.</summary>
    private const string Legacy = "static int? Add(int? a, int b) { if (a.HasValue) { return new int?(a.GetValueOrDefault() + b); } return null; }";

    private const string Modern = "static int? Add(int? a, int b) => a + b;";

    private static readonly SourceSpan Span = new("C.cs", 1, 1, 1, 2);

    private static readonly string[] Sides = ["legacy", "modern"];

    private static readonly Func<IMethodSymbol, Compilation, bool, ImmutableHashSet<string>, IrProcedure> Never =
        static (_, _, _, _) => throw new InvalidOperationException("the pair must not be lowered again");

    [Fact]
    public void ACongruentPairIsNeverRelowered()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        Assert.True(IlFallback.Unshared(legacy.Body, modern.Body) > 0);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: true, NullRunLog.Instance, Never);

        Assert.Equal(new IlFallback.Choice(legacy.Body, modern.Body, Tried: false, IlFallback.Operation), choice);
    }

    [Fact]
    public void APairWithoutAnUnsharedOpaqueIsNeverRelowered()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Legacy);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, Never);

        Assert.Equal(new IlFallback.Choice(legacy.Body, modern.Body, Tried: false, IlFallback.Operation), choice);
    }

    [Fact]
    public void BothSidesAreRelowered()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        List<(Compilation Compilation, bool X87)> read = [];

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, (method, compilation, x87, _) =>
        {
            read.Add((compilation, x87));
            return IlLowerer.Lower(method, compilation, x87);
        });

        Assert.Equal([(legacy.Compilation, false), (modern.Compilation, false)], read);
        Assert.Equal(IlFallback.Il, choice.Lowering);
        Assert.True(choice.Tried);
        Assert.Equal(IrText.Dump(IlLowerer.Lower(legacy.Symbol, legacy.Compilation)), IrText.Dump(choice.Old));
        Assert.Equal(IrText.Dump(IlLowerer.Lower(modern.Symbol, modern.Compilation)), IrText.Dump(choice.New));
        Assert.Equal(0, IlFallback.Unshared(choice.Old, choice.New));
        // Criterion 7: a model of the IL bodies names the parameters a model of the IOperation bodies does (ADR 0021).
        Assert.Equal(legacy.Body.Parameters, choice.Old.Parameters);
        Assert.Equal(modern.Body.Parameters, choice.New.Parameters);
    }

    /// <summary>The legacy side of a project whose floating point runs on x87 is read as <see cref="IrLowerer"/> reads it (ticket M4-002).</summary>
    [Fact]
    public void TheLegacySideIsReadWithItsX87Flag()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        legacy = legacy with { Compilation = legacy.Compilation.WithOptions(legacy.Compilation.Options.WithPlatform(Platform.X86)) };
        legacy = legacy with { Symbol = Method(legacy.Compilation) };
        List<bool> x87s = [];

        _ = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, (method, compilation, x87, _) =>
        {
            x87s.Add(x87);
            return IlLowerer.Lower(method, compilation, x87);
        });

        Assert.Equal([true, false], x87s);
    }

    [Fact]
    public void IlIsKeptOnlyWithFewerUnsharedOpaques()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        IrProcedure Same(IMethodSymbol method, Compilation compilation, bool x87, ImmutableHashSet<string> rebound) => ReferenceEquals(compilation, legacy.Compilation) ? legacy.Body : modern.Body;
        IrProcedure Fewer(IMethodSymbol method, Compilation compilation, bool x87, ImmutableHashSet<string> rebound) => IlLowerer.Lower(method, compilation, x87);

        IlFallback.Choice same = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, Same);
        IlFallback.Choice fewer = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, Fewer);

        Assert.Equal(new IlFallback.Choice(legacy.Body, modern.Body, Tried: true, IlFallback.Operation), same);
        Assert.Equal(IlFallback.Il, fewer.Lowering);
    }

    [Theory]
    [InlineData(true, "legacy")]
    [InlineData(false, "modern")]
    public void AnUnreadableMethodKeepsItsOperationLowering(bool legacyUnreadable, string side)
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        Compilation unreadable = legacyUnreadable ? legacy.Compilation : modern.Compilation;
        RecordingRunLog log = new(isDebug: true);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, log, (method, compilation, x87, _) =>
            ReferenceEquals(compilation, unreadable)
                ? IrLowerer.Opaque(IlLowerer.Lower(method, compilation, x87), IlAstReader.EmitFailed, Span)
                : IlLowerer.Lower(method, compilation, x87));

        Assert.Equal(new IlFallback.Choice(legacy.Body, modern.Body, Tried: true, IlFallback.Operation), choice);
        Assert.Equal([$"detail il-fallback: {side} {legacy.Body.Identity.Value} keeps its operation lowering: il-emit-failed"], log.Events);
    }

    /// <summary>Each of <see cref="IlAstReader"/>'s reasons for having no ILAst keeps the pair's IOperation lowering.</summary>
    [Theory]
    [InlineData(IlAstReader.EmitFailed)]
    [InlineData(IlAstReader.MethodNotFound)]
    [InlineData(IlAstReader.NoBody)]
    public void EveryReadFailureIsUnreadable(string reason)
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        IrProcedure Failed(IMethodSymbol method, Compilation compilation, bool x87, ImmutableHashSet<string> rebound) =>
            IrLowerer.Opaque(IlLowerer.Lower(method, compilation, x87), reason, Span);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, Failed);

        Assert.Equal(IlFallback.Operation, choice.Lowering);
    }

    /// <summary>A whole-body opaque for any other reason is a lowering like any other, and counts as its one unshared opaque.</summary>
    [Fact]
    public void AnotherWholeBodyOpaqueIsNoReadFailure()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, "static int? Add(int? a, int b) => (a + b) + (a - b) + (a * b);");
        Assert.True(IlFallback.Unshared(legacy.Body, modern.Body) > 1);
        IrProcedure Lowered(IMethodSymbol method, Compilation compilation, bool x87, ImmutableHashSet<string> rebound) => ReferenceEquals(compilation, legacy.Compilation)
            ? IlLowerer.Lower(method, compilation, x87)
            : IrLowerer.Opaque(modern.Body, "UnboxAny", Span);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, Lowered);

        Assert.Equal(IlFallback.Il, choice.Lowering);
        Assert.Equal(1, IlFallback.Unshared(choice.Old, choice.New));
    }

    /// <summary>An <c>async</c> or iterator method's IL is its state machine's kickoff: neither side is read, and each is one debug line.</summary>
    [Theory]
    [InlineData("static async System.Threading.Tasks.Task<int?> Add(int? a, int b) { await System.Threading.Tasks.Task.Yield(); ", "return null; }", "return a + b; }")]
    [InlineData("static System.Collections.Generic.IEnumerable<int?> Add(int? a, int b) { yield return null; ", "}", "yield return a + b; }")]
    public void AStateMachineKeepsItsOperationLowering(string start, string legacyEnd, string modernEnd)
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(start + legacyEnd, start + modernEnd);
        RecordingRunLog log = new(isDebug: true);

        IlFallback.Choice choice = IlFallback.Choose(legacy, modern, congruent: false, log, Never);

        Assert.Equal(new IlFallback.Choice(legacy.Body, modern.Body, Tried: true, IlFallback.Operation), choice);
        Assert.Equal(
            [.. Sides.Select(side => $"detail il-fallback: {side} {legacy.Body.Identity.Value} keeps its operation lowering: il-state-machine")],
            log.Events);
    }

    [Fact]
    public void WithoutDebugAnUnreadableMethodWritesNothing()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        RecordingRunLog log = new();

        _ = IlFallback.Choose(legacy, modern, congruent: false, log, (method, compilation, x87, _) =>
            IrLowerer.Opaque(IlLowerer.Lower(method, compilation, x87), IlAstReader.NoBody, Span));

        Assert.Empty(log.Events);
    }

    /// <summary>An opaque is unshared when it has no fingerprint, or the other side has none like it (ADR 0024 decision 2).</summary>
    [Fact]
    public void UnsharedCountsTheOpaquesTheOtherSideLacks()
    {
        IrProcedure body = Pair(Legacy, Legacy).Legacy.Body;
        IrProcedure With(params IrOpaque[] opaques) =>
            body with { Blocks = [body.Blocks[0] with { Instructions = [.. opaques, .. body.Blocks[0].Instructions] }, .. body.Blocks.Skip(1)] };
        IrOpaque shared = new(Target: null, "Conversion", Span) { Fingerprint = "shared" };
        IrOpaque legacyOnly = new(Target: null, "Conversion", Span) { Fingerprint = "legacy" };
        IrOpaque plain = new(Target: null, "Binary", Span);

        Assert.Equal(0, IlFallback.Unshared(With(shared), With(shared)));
        Assert.Equal(1, IlFallback.Unshared(With(shared, legacyOnly), With(shared)));
        Assert.Equal(3, IlFallback.Unshared(With(plain, legacyOnly), With(plain)));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);

        Assert.Throws<ArgumentNullException>("legacy", () => IlFallback.Choose(null!, modern, congruent: false, NullRunLog.Instance));
        Assert.Throws<ArgumentNullException>("modern", () => IlFallback.Choose(legacy, null!, congruent: false, NullRunLog.Instance));
        Assert.Throws<ArgumentNullException>("log", () => IlFallback.Choose(legacy, modern, congruent: false, null!));
        Assert.Throws<ArgumentNullException>("lower", () => IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance, null!));
        Assert.Throws<ArgumentNullException>("old", () => IlFallback.Unshared(null!, legacy.Body));
        Assert.Throws<ArgumentNullException>("@new", () => IlFallback.Unshared(legacy.Body, null!));
    }

    /// <summary>The production overload reads the real IL: the lifted operator's pair keeps the IL bodies.</summary>
    /// <summary>ADR 0042 (ticket P2-069): each side is read from IL with its own rebound callee identities.</summary>
    [Fact]
    public void EachSideIsReadWithItsReboundIdentities()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);
        List<(Compilation Compilation, string Rebound)> read = [];

        _ = IlFallback.Choose(legacy with { Rebound = ["A::F()"] }, modern with { Rebound = ["B::F()"] }, congruent: false, NullRunLog.Instance, (method, compilation, x87, rebound) =>
        {
            read.Add((compilation, Assert.Single(rebound)));
            return IlLowerer.Lower(method, compilation, x87, rebound);
        });

        Assert.Equal([(legacy.Compilation, "A::F()"), (modern.Compilation, "B::F()")], read);
        Assert.Empty(Pair(Legacy, Modern).Legacy.Rebound);
    }

    [Fact]
    public void TheProductionLoweringReadsTheIl()
    {
        (IlFallback.Side legacy, IlFallback.Side modern) = Pair(Legacy, Modern);

        Assert.Equal(IlFallback.Il, IlFallback.Choose(legacy, modern, congruent: false, NullRunLog.Instance).Lowering);
    }

    private static (IlFallback.Side Legacy, IlFallback.Side Modern) Pair(string legacy, string modern) => (Side(legacy, legacy: true), Side(modern, legacy: false));

    private static IlFallback.Side Side(string member, bool legacy)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"class C\n{{\n{member}\n}}\n");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IMethodSymbol method = Method(compilation);
        return new IlFallback.Side(method, compilation, CSharpFrontend.LowerWithIrLowerer(method, compilation, EquivConfig.Default, legacy).Body);
    }

    private static IMethodSymbol Method(Compilation compilation) => compilation.GetTypeByMetadataName("C")!.GetMembers("Add").OfType<IMethodSymbol>().Single();
}
