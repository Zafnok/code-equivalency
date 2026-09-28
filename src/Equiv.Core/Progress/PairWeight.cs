using Equiv.Core.Ir;

namespace Equiv.Core.Progress;

/// <summary>
/// How much of the verify phase's time a matched pair is expected to take (ADR 0038). A pair decided without the solver
/// (unbound, an async mismatch, or congruent) weighs 1. A pair the solver decides weighs the instructions and blocks of
/// both sides, times <see cref="LoopFactor"/> when either side has a back edge, because such a pair climbs the loop ladder.
/// </summary>
public static class PairWeight
{
    /// <summary>How much more a pair with a loop is expected to cost than a loop-free pair of the same size.</summary>
    public const long LoopFactor = 20;

    /// <summary>The rungs of the loop ladder a looping pair can climb (<c>LoopLadder.Climb</c>: bounded, lockstep, k-induction, Spacer, invariant).</summary>
    public const int LadderRungs = 5;

    /// <summary>The weight of the pair <paramref name="old"/>, <paramref name="new"/>; <paramref name="solver"/> says whether the solver decides it.</summary>
    public static long Of(IrProcedure old, IrProcedure @new, bool solver)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);

        if (!solver)
        {
            return 1;
        }

        long size = Size(old) + Size(@new);
        return Loops(old, @new) ? size * LoopFactor : size;
    }

    /// <summary>The rungs the pair can take in the worst case: 1 for a loop-free pair, <see cref="LadderRungs"/> when either side loops.</summary>
    public static int Rungs(IrProcedure old, IrProcedure @new)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(@new);

        return Loops(old, @new) ? LadderRungs : 1;
    }

    private static bool Loops(IrProcedure old, IrProcedure @new) =>
        !IrLoopAnalysis.Of(old).Loops.IsEmpty || !IrLoopAnalysis.Of(@new).Loops.IsEmpty;

    private static long Size(IrProcedure body) => body.Blocks.Sum(static b => b.Instructions.Length + 1L);
}
