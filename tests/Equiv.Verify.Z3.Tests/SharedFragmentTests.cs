using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Shared opaque fragments (ADR 0024 decision 2; ticket M4-004 criteria 3 and 4): a fingerprint on both sides is one call
/// <c>opaque:&lt;fingerprint&gt;</c> over the fragment's reads, so the paths through it are decided; a fingerprint on one side
/// only keeps ADR 0014's meaning; and a divergence that depends on the shared call is Unknown(Abstraction), pointing at the
/// fragment's line on each side.
/// </summary>
public sealed class SharedFragmentTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    private static readonly SourceSpan OldSpan = new("Old.cs", 3, 9, 3, 30);

    private static readonly SourceSpan NewSpan = new("New.cs", 4, 9, 4, 30);

    /// <summary>The fragment beside an integer branch that changed its spelling only: <c>b &lt; 1</c> against <c>b &lt;= 0</c>.</summary>
    [Fact]
    public void FragmentOnBothSidesIsASharedCall()
    {
        Verdict verdict = Verify(Fragment("Old.cs", "f1", "slt", 1), Fragment("New.cs", "f1", "sle", 0));

        Assert.IsType<Equivalent>(verdict);
    }

    /// <summary>A change on the integer path that skips the fragment is Divergent: the difference depends on no abstraction.</summary>
    [Fact]
    public void ADivergenceBesideASharedFragmentIsDivergent()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Fragment("Old.cs", "f1", "slt", 1), Fragment("New.cs", "f1", "slt", 1, early: 8)));

        Assert.All(
            new[] { divergent.Counterexample.Old, divergent.Counterexample.New },
            static run => Assert.DoesNotContain(run.Trace, static c => string.Equals(c.Callee.Value, "opaque:f1", StringComparison.Ordinal)));
    }

    [Fact]
    public void FragmentOnOneSideStaysOpaque()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(Fragment("Old.cs", "f1", "slt", 1), Fragment("New.cs", "f2", "slt", 1)));

        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal([OldSpan, NewSpan], unknown.Causes.Select(static c => c.Span));
    }

    /// <summary>
    /// The same fragment moved across a call: the traces differ first at an event of the shared call, which the replay
    /// taints (ticket M3-016), so the result is Unknown(Abstraction) and points at the fragment on each side.
    /// </summary>
    [Fact]
    public void ReorderedSharedFragmentIsUnknownAbstraction()
    {
        Unknown unknown = Assert.IsType<Unknown>(Verify(
            """
            proc "T::M(int, int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              call "Log::Write(int)"(%b)
              %r: bv32 = opaque "DelegateCreation" at "Old.cs" 3:9-3:30 fragment "f1" reads(%a)
              ret %r
            """,
            """
            proc "T::M(int, int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = opaque "DelegateCreation" at "New.cs" 4:9-4:30 fragment "f1" reads(%a)
              call "Log::Write(int)"(%b)
              ret %r
            """));

        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal([(Codebase.Legacy, OldSpan), (Codebase.Modern, NewSpan)], unknown.Causes.Select(static c => (c.Side, c.Span)));
        Assert.All(unknown.Abstractions, static a => Assert.Equal("opaque:f1", a.Identity.Value));
    }

    [Fact]
    public void ShareFragmentsRewritesOnlyFingerprintsOnBothSides()
    {
        (IrProcedure old, IrProcedure @new) = Pair(Fragment("Old.cs", "f1", "slt", 1), Fragment("New.cs", "f2", "slt", 1));
        (IrProcedure sharedOld, _, _) = ProductEncoder.ShareFragments(old, old);
        (IrProcedure unsharedOld, IrProcedure unsharedNew, _) = ProductEncoder.ShareFragments(old, @new);

        IrCall call = Assert.Single(sharedOld.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());
        IrOpaque fragment = Assert.Single(old.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>());
        Assert.Equal(new IrCall(fragment.Target, fragment.Threw, new CallIdentity("opaque:f1"), fragment.Reads) { Heap = fragment.Heap }, call);
        Assert.Equal(old, unsharedOld);
        Assert.Equal(@new, unsharedNew);
    }

    [Fact]
    public void LocateGivesEachSideItsSpanAndLeavesAnythingElseAlone()
    {
        (IrProcedure old, IrProcedure @new) = Pair(Fragment("Old.cs", "f1", "slt", 1), Fragment("New.cs", "f1", "slt", 1));
        ProductEncoder.SharedFragments shared = ProductEncoder.ShareFragments(old, @new);
        Abstraction legacy = new(Codebase.Legacy, new CallIdentity("opaque:f1"), Span: null);
        Abstraction spanned = legacy with { Span = NewSpan };
        Abstraction pure = new(Codebase.Legacy, new CallIdentity("dec.add"), Span: null);

        Assert.Equal(OldSpan, shared.Locate(legacy).Span);
        Assert.Equal(NewSpan, shared.Locate(legacy with { Side = Codebase.Modern }).Span);
        Assert.Equal(spanned, shared.Locate(spanned));
        Assert.Equal(pure, shared.Locate(pure));
    }

    /// <summary>
    /// <c>if (b op constant) return early; r = fragment(a); return r;</c>, the fragment's flag throwing <c>System.Exception</c>
    /// and its heap pair writing <c>field.T.f</c>, as the lowerer emits it. The fragment is at line 3 of <c>Old.cs</c> or line 4
    /// of any other path. The early return reaches no fragment: every path through one branches on its <c>threw</c> flag,
    /// which the replay taints, so a divergence past a shared fragment is Unknown(Abstraction) (ticket M3-016).
    /// </summary>
    private static string Fragment(string path, string fingerprint, string op, int constant, int early = 7)
    {
        string span = string.Equals(path, "Old.cs", StringComparison.Ordinal) ? "3:9-3:30" : "4:9-4:30";
        return $$"""
            proc "T::M(int, int)" (%a: bv32, %b: bv32, ref %field.T.f: map<sort "T", bv32>) -> bv32 entry B0
            B0:
              %k: bv32 = const bv32 {{constant.ToString(CultureInfo.InvariantCulture)}}
              %c: bool = {{op}} %b, %k
              br %c, B1, B2
            B1:
              %early: bv32 = const bv32 {{early.ToString(CultureInfo.InvariantCulture)}}
              ret %early outs(%field.T.f = %field.T.f)
            B2:
              %r: bv32 = opaque "DelegateCreation" at "{{path}}" {{span}} fragment "{{fingerprint}}" reads(%a) threw %t: bool heap("field.T.f" %field.T.f -> %f1: map<sort "T", bv32>)
              br %t, B3, B4
            B3:
              throw "System.Exception" outs(%field.T.f = %f1)
            B4:
              ret %r outs(%field.T.f = %f1)
            """;
    }

    private static (IrProcedure Old, IrProcedure New) Pair(string old, string @new) => Fixture.Pair(old + "\n---\n" + @new);

    private static Verdict Verify(string old, string @new)
    {
        (IrProcedure o, IrProcedure n) = Pair(old, @new);
        return new Z3Backend().Verify(o, n, Options);
    }
}
