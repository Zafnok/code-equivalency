using Equiv.Core.Ir;
using Equiv.Core.Progress;
using Equiv.Core.Tests.Ir;

using Xunit;

namespace Equiv.Core.Tests.Progress;

/// <summary>Ticket M4-012 criterion 7: a pair the solver never sees weighs 1, and the loop factor applies exactly when a back edge exists.</summary>
public sealed class PairWeightTests
{
    /// <summary>Two blocks, two instructions, no back edge: size 4 per side.</summary>
    private static readonly IrProcedure Straight = IrText.Parse("""
        proc "T::Next(int)" (%n "n": bv32) -> bv32 entry B0
        B0:
          %one: bv32 = const bv32 1
          %m: bv32 = add %n, %one
          goto B1
        B1:
          ret %m
        """);

    /// <summary><see cref="LoopFixtures.Single"/>: four blocks, seven instructions, one back edge: size 11 per side.</summary>
    private static readonly IrProcedure Looping = IrText.Parse(LoopFixtures.Single);

    [Fact]
    public void APairTheSolverNeverSeesWeighsOne()
    {
        Assert.Equal(1, PairWeight.Of(Looping, Looping, solver: false));
        Assert.Equal(1, PairWeight.Of(Straight, Straight, solver: false));
    }

    [Fact]
    public void ALoopFreePairWeighsItsInstructionsAndBlocks()
    {
        Assert.Equal(4 + 4, PairWeight.Of(Straight, Straight, solver: true));
        Assert.Equal(1, PairWeight.Rungs(Straight, Straight));
    }

    [Fact]
    public void ABackEdgeOnEitherSideAppliesTheLoopFactor()
    {
        Assert.Equal((11 + 4) * PairWeight.LoopFactor, PairWeight.Of(Looping, Straight, solver: true));
        Assert.Equal((4 + 11) * PairWeight.LoopFactor, PairWeight.Of(Straight, Looping, solver: true));
        Assert.Equal((11 + 11) * PairWeight.LoopFactor, PairWeight.Of(Looping, Looping, solver: true));
        Assert.Equal(PairWeight.LadderRungs, PairWeight.Rungs(Looping, Straight));
        Assert.Equal(PairWeight.LadderRungs, PairWeight.Rungs(Straight, Looping));
    }

    [Fact]
    public void NullBodiesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => PairWeight.Of(null!, Straight, solver: true));
        Assert.Throws<ArgumentNullException>(() => PairWeight.Of(Straight, null!, solver: true));
        Assert.Throws<ArgumentNullException>(() => PairWeight.Rungs(null!, Straight));
        Assert.Throws<ArgumentNullException>(() => PairWeight.Rungs(Straight, null!));
    }
}
