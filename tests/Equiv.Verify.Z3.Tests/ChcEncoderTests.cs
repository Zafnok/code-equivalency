using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-001: <see cref="ChcEncoder.Solves"/> on answers that define no relation, whatever Spacer's own answers
/// happen to leave out. Each relation then reads as false, and as true once a rule concludes it from premises that hold.
/// </summary>
public sealed class ChcEncoderTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>
    /// <c>irreducible</c> never leaves its cycle: reading the loop pairs its rules reach as true and the rest as false
    /// solves the clauses with wrap-around arithmetic.
    /// </summary>
    [Fact]
    public void ARelationNoAnswerDefinesReadsAsTrueOnceARuleConcludesIt()
    {
        Fixture fixture = Fixture.Load("loops/irreducible");
        using Context context = new();
        ChcEncoder wrapping = new(context, fixture.Old, fixture.New, ChcArithmetic.WrappingIntegers, []);

        Assert.True(wrapping.Solves(context.MkTrue(), Options));
    }

    /// <summary>
    /// <c>trip-count-changed</c> diverges: once every relation its rules reach reads as true, a rule concludes <c>bad</c>,
    /// so no reading of the relations it left out solves the clauses.
    /// </summary>
    [Fact]
    public void AnAnswerThatLeavesADivergenceDerivableIsRejected()
    {
        Fixture fixture = Fixture.Load("loops/trip-count-changed");
        using Context context = new();
        ChcEncoder wrapping = new(context, fixture.Old, fixture.New, ChcArithmetic.WrappingIntegers, []);

        Assert.False(wrapping.Solves(context.MkTrue(), Options));
    }

    /// <summary>
    /// Ticket P2-050 criterion 2: the fixedpoint of <see cref="ChcEncoder.Query"/> and the solver of each check stop when
    /// they have spent <see cref="VerificationOptions.ResourceLimit"/>, long before the timeout: Spacer gives up with Z3's
    /// resource message on a pair it otherwise proves, <see cref="ChcEncoder.Solves"/> does not admit an answer it
    /// otherwise admits, <see cref="ChcEncoder.Refutes"/> gives up on its first obligation, and
    /// <see cref="ChcEncoder.DerivationInputs"/> has no model to read.
    /// </summary>
    [Fact]
    public void EverySolverSetsTheResourceLimit()
    {
        VerificationOptions starved = Options with { TimeoutMs = 600_000, ResourceLimit = 1 };
        Fixture fusion = Fixture.Load("loops/fusion");
        Fixture irreducible = Fixture.Load("loops/irreducible");

        // An opaque node before the loop on one side: bad is derived from the entry rule, through no relation.
        (IrProcedure opaqueOld, IrProcedure opaqueNew) = Fixture.Pair("""
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
        using Context context = new();
        ChcEncoder integers = new(context, fusion.Old, fusion.New, ChcArithmetic.Integers, []);
        ChcEncoder wrapping = new(context, irreducible.Old, irreducible.New, ChcArithmetic.WrappingIntegers, []);
        ChcEncoder entry = new(context, opaqueOld, opaqueNew, ChcArithmetic.Integers, []);
        Dictionary<FuncDecl, (Expr[] Parameters, BoolExpr Body)> definitions = integers.Relations.ToDictionary(static r => r.Decl, r => ((Expr[])[.. r.Parameters], context.MkTrue()));

        ChcEncoder.ChcAnswer answer = integers.Query(overflows: false, starved);

        Assert.Equal(Status.UNSATISFIABLE, integers.Query(overflows: false, Options).Status);
        Assert.Equal(Status.UNKNOWN, answer.Status);
        Assert.Equal("max. resource limit exceeded", answer.Reason);
        Assert.True(wrapping.Solves(context.MkTrue(), Options));
        Assert.False(wrapping.Solves(context.MkTrue(), starved));
        Assert.Equal("Z3 gave up on the init obligation: max. resource limit exceeded", integers.Refutes(definitions, starved)?.Reason);
        Assert.StartsWith("the exit obligation fails: ", integers.Refutes(definitions, Options)?.Reason, StringComparison.Ordinal);
        Assert.NotNull(entry.DerivationInputs(context.MkTrue(), Options));
        Assert.Throws<Z3Exception>(() => entry.DerivationInputs(context.MkTrue(), starved));
    }
}
