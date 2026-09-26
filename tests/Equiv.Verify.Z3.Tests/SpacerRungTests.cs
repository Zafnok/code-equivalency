using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Xunit;

using Rung = Equiv.Verify.Z3.LoopLadder.Rung;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Rung 4 on its own (ticket P1-001): <see cref="SpacerRung"/> called on a pair directly, so no earlier rung decides it
/// first. The fixtures under <c>Fixtures/loops/</c> cover the ladder's paths through it, and name the arithmetic each
/// answer holds in.
/// </summary>
public sealed class SpacerRungTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    [Fact]
    public void WithIntegerModeOffRungFourAsksOverTheBitVectors()
    {
        Fixture fixture = Fixture.Load("loops/counter-shape");

        Rung rung = Prove(fixture.Old, fixture.New, Options with { ChcIntMode = false });

        Assert.IsType<Equivalent>(rung.Verdict);
        Assert.Equal(ChcMode.BitVectors, rung.Step.Mode);
        Assert.Equal("Spacer found a coupling invariant over the bitvectors, since integer mode is off", rung.Step.Detail);
    }

    [Fact]
    public void ADerivationThroughASortLiteralReplaysWithTheLiteralsElement()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::IsFirst(string)" (%s: sort "S") -> bool entry B0
            B0:
              %a: sort "S" = const sort "S" 1
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %two: bv32 = const bv32 2
              goto B1
            B1:
              %i: bv32 = phi [B0: %z, B2: %i1]
              %c: bool = slt %i, %two
              br %c, B2, B3
            B2:
              %i1: bv32 = add %i, %one
              goto B1
            B3:
              %e: bool = eq %s, %a
              ret %e
            ---
            proc "T::IsFirst(string)" (%s: sort "S") -> bool entry B0
            B0:
              %f: bool = const bool false
              ret %f
            """);

        Divergent divergent = Assert.IsType<Divergent>(Prove(old, @new, Options).Verdict);

        Assert.Equal([new IrSortValue("S", 1)], divergent.Counterexample.Inputs.Arguments);
    }

    [Fact]
    public void AReturnTypeChangeDivergesWhenBothSidesReturn()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Two()" () -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %two: bv32 = const bv32 2
              goto B1
            B1:
              %i: bv32 = phi [B0: %z, B2: %i1]
              %c: bool = slt %i, %two
              br %c, B2, B3
            B2:
              %i1: bv32 = add %i, %one
              goto B1
            B3:
              ret %i
            ---
            proc "T::Two()" () -> bv64 entry B0
            B0:
              %two: bv64 = const bv64 2
              ret %two
            """);

        Rung rung = Prove(old, @new, Options);

        Assert.IsType<Divergent>(rung.Verdict);
        Assert.Equal((ProofMethod.Chc, RungOutcome.Refuted, ChcMode.Integers), (rung.Step.Rung, rung.Step.Outcome, rung.Step.Mode));
    }

    /// <summary>
    /// An opaque node before the loop, on one side only: the one derivation of <c>bad</c> is the entry segment's own rule,
    /// so no relation's fact holds its inputs and they come from a model of that rule, and its replay reaches the node,
    /// which is Unknown(Opaque) with the node as the cause.
    /// </summary>
    [Fact]
    public void AnOpaqueNodeBeforeTheLoopIsUnknownOpaqueThroughTheEntrysDerivation()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Log(int)" (%n "n": bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %early: bool = slt %n, %z
              br %early, B3, B1
            B3:
              opaque "EarlyEffect" at "T.cs" 3:9-3:30
              goto B1
            B1:
              %i "i": bv32 = phi [B0: %z, B3: %z, B2: %i1]
              %c: bool = slt %i, %n
              br %c, B2, B4
            B2:
              %i1 "i": bv32 = add %i, %one
              goto B1
            B4:
              ret %i
            ---
            proc "T::Log(int)" (%n "n": bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              goto B1
            B1:
              %i "i": bv32 = phi [B0: %z, B2: %i1]
              %c: bool = slt %i, %n
              br %c, B2, B4
            B2:
              %i1 "i": bv32 = add %i, %one
              goto B1
            B4:
              ret %i
            """);

        Rung rung = Prove(old, @new, Options);

        Unknown unknown = Assert.IsType<Unknown>(rung.Verdict);
        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Equal(Codebase.Legacy, Assert.Single(unknown.Causes).Side);
        Assert.StartsWith("a derivation over the integers reaches an opaque node", rung.Step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReplayOracleRefusesACall() =>
        Assert.Throws<InvalidOperationException>(static () => SpacerRung.NoCalls.Instance.Answer(new CallIdentity("T::M()"), [], resultType: null, position: 0, []));

    private static Rung Prove(IrProcedure old, IrProcedure @new, VerificationOptions options) =>
        new SpacerRung(static () => new Context(), options).Prove(old, @new);
}
