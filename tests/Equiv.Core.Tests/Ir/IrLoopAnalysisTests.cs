using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary><see cref="IrLoopAnalysis"/> on the <see cref="LoopFixtures"/> and a few degenerate shapes (ticket M3-002 deliverable 1).</summary>
public sealed class IrLoopAnalysisTests
{
    private static readonly IrBlockId B0 = new(0);
    private static readonly IrBlockId B1 = new(1);
    private static readonly IrBlockId B2 = new(2);
    private static readonly IrBlockId B3 = new(3);
    private static readonly IrBlockId B4 = new(4);
    private static readonly IrBlockId B5 = new(5);

    [Fact]
    public void ASingleLoopHasItsHeaderBodyLatchAndGuardExit()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse(LoopFixtures.Single));

        Assert.Equal([new IrLoop(B1, [B1, B2], [B2], [(B1, B3)], Parent: null)], analysis.Loops);
        Assert.Equal([B0, B1, B3, B2], analysis.ReversePostorder.Select(static b => b.Id));
        Assert.True(analysis.IsReducible);
        Assert.False(analysis.IsSelfRecursive);
    }

    [Fact]
    public void NestedLoopsFormAForestListedOuterFirst()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse(LoopFixtures.Nested));

        Assert.Equal(
            [
                new IrLoop(B1, [B1, B2, B4, B3], [B4], [(B1, B5)], Parent: null),
                new IrLoop(B2, [B2, B3], [B3], [(B2, B4)], Parent: B1),
            ],
            analysis.Loops);
    }

    [Fact]
    public void AnEarlyReturnIsAnExitEdge()
    {
        IrLoop loop = Assert.Single(IrLoopAnalysis.Of(IrText.Parse(LoopFixtures.EarlyReturn)).Loops);

        Assert.Equal([B1, B2, B4], loop.Blocks);
        Assert.Equal([(B1, B5), (B2, B3)], loop.Exits);
    }

    [Fact]
    public void AThrowIsAnExitEdge()
    {
        IrLoop loop = Assert.Single(IrLoopAnalysis.Of(IrText.Parse(LoopFixtures.Throws)).Loops);

        Assert.Equal([(B1, B5), (B2, B3)], loop.Exits);
    }

    [Fact]
    public void SiblingLoopsAreOrderedByTheirHeaders()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::M(bool)" (%c: bool) entry B0
            B0:
              goto B1
            B1:
              br %c, B1, B2
            B2:
              br %c, B2, B3
            B3:
              ret
            """));

        Assert.Equal([B1, B2], analysis.Loops.Select(static l => l.Header));
        Assert.All(analysis.Loops, static l => Assert.Null(l.Parent));
    }

    [Fact]
    public void TwoBackEdgesToOneHeaderAreOneLoop()
    {
        IrLoop loop = Assert.Single(IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::M(bool)" (%c: bool) entry B0
            B0:
              goto B1
            B1:
              br %c, B2, B3
            B2:
              br %c, B1, B1
            B3:
              br %c, B1, B4
            B4:
              ret
            """)).Loops);

        Assert.Equal([B1, B3, B2], loop.Blocks);
        Assert.Equal([B3, B2], loop.Latches);
        Assert.Equal([(B3, B4)], loop.Exits);
    }

    [Fact]
    public void AnEntryThatIsAHeaderIsReducible()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::M(bool)" (%c: bool) entry B0
            B0:
              br %c, B0, B1
            B1:
              ret
            """));

        Assert.Equal([new IrLoop(B0, [B0], [B0], [(B0, B1)], Parent: null)], analysis.Loops);
        Assert.True(analysis.IsReducible);
    }

    [Fact]
    public void ACycleWithTwoEntriesIsIrreducible()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::M(bool)" (%c: bool) entry B0
            B0:
              br %c, B1, B2
            B1:
              br %c, B2, B3
            B2:
              goto B1
            B3:
              ret
            """));

        Assert.False(analysis.IsReducible);
    }

    [Fact]
    public void AcyclicCodeHasNoLoopsAndUnreachableBlocksAreIgnored()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::M()" () entry B0
            B0:
              ret
            B1:
              goto B1
            """));

        Assert.Empty(analysis.Loops);
        Assert.Equal([B0], analysis.ReversePostorder.Select(static b => b.Id));
    }

    [Fact]
    public void ACallToItselfIsSelfRecursion()
    {
        IrLoopAnalysis analysis = IrLoopAnalysis.Of(IrText.Parse("""
            proc "T::F(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = call "T::G(int)"(%a)
              %s: bv32 = call "T::F(int)"(%r)
              ret %s
            """));

        Assert.True(analysis.IsSelfRecursive);
        Assert.Empty(analysis.Loops);
    }

    [Fact]
    public void LoopsCompareStructurally()
    {
        IrLoop loop = new(B1, [B1, B2], [B2], [(B1, B3)], Parent: null);
        IrLoop same = new(B1, [B1, B2], [B2], [(B1, B3)], Parent: null);

        Assert.Equal(loop, same);
        Assert.Equal(loop.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(loop, same with { Parent = B0 });
        Assert.False(loop.Equals(Null.Of<IrLoop>()));
    }

    [Fact]
    public void ANullProcedureIsRejected()
    {
        Assert.Throws<ArgumentNullException>(static () => IrLoopAnalysis.Of(null!));
    }
}
