using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.TestSupport;

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
        RecordingRunLog log = new(isDebug: true);
        Assert.NotNull(entry.DerivationInputs(context.MkTrue(), Options with { Log = log }));
        Assert.Equal(["check:derivation=sat"], BackendProgressTests.Details(log));
        Assert.Throws<Z3Exception>(() => entry.DerivationInputs(context.MkTrue(), starved));
    }

    /// <summary>
    /// Ticket P2-050: a Spacer query gets ten times the resource limit, since Z3 counts its steps far cheaper than a
    /// product query's, and no more than Z3's parameter holds. <c>loops/fusion</c> needs between 2,000,000 and
    /// 3,000,000 units, so it is proved at a limit of 1,000,000 and not at 10,000.
    /// </summary>
    [Fact]
    public void ASpacerQueryGetsTenTimesTheResourceLimit()
    {
        Fixture fusion = Fixture.Load("loops/fusion");
        using Context context = new();
        ChcEncoder integers = new(context, fusion.Old, fusion.New, ChcArithmetic.Integers, []);

        Assert.Equal(50_000_000u, ChcEncoder.SpacerResourceLimit(Options with { ResourceLimit = 5_000_000 }));
        Assert.Equal(uint.MaxValue, ChcEncoder.SpacerResourceLimit(Options with { ResourceLimit = int.MaxValue }));
        Assert.Equal(Status.UNKNOWN, integers.Query(overflows: false, Options with { TimeoutMs = 600_000, ResourceLimit = 10_000 }).Status);
        Assert.Equal(Status.UNSATISFIABLE, integers.Query(overflows: false, Options with { TimeoutMs = 600_000, ResourceLimit = 1_000_000 }).Status);
    }

    /// <summary>
    /// Ticket P2-100 criterion 2: a Spacer query spends the same <c>rlimit</c> and gives the same answer whatever terms
    /// were freed in the encoder's context before it was encoded, by <c>Dispose</c> or by a garbage collection. Freed
    /// terms change the numbers of the encoder's terms, and Spacer run in that context spent a different amount.
    /// </summary>
    [Theory]
    [InlineData("loops/fusion")]
    [InlineData("loops/nesting-changed")]
    [InlineData("loops/trip-count-changed")]
    public void TheSameQuerySpendsTheSameResourceWhateverWasCollected(string name)
    {
        Fixture fixture = Fixture.Load(name);

        (Status Status, uint Spent, string Answer) undisturbed = Spend(fixture, static _ => { });
        (Status Status, uint Spent, string Answer) disposed = Spend(fixture, static context =>
        {
            Expr[] freed = [.. Enumerable.Range(0, 200).Select(i => context.MkIntConst("freed" + i.ToString(CultureInfo.InvariantCulture)))];
            foreach (Expr term in freed.Where(static (_, i) => i % 3 != 2))
            {
                term.Dispose();
            }
        });
        (Status Status, uint Spent, string Answer) collected = Spend(fixture, static context =>
        {
            Unreferenced(context);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        });

        Assert.NotEqual(Status.UNKNOWN, undisturbed.Status);
        Assert.NotEqual(0u, undisturbed.Spent);
        Assert.Equal(undisturbed, disposed);
        Assert.Equal(undisturbed, collected);
    }

    /// <summary>The fixture's divergence query over the integers, encoded after <paramref name="disturb"/> has had the context.</summary>
    private static (Status Status, uint Spent, string Answer) Spend(Fixture fixture, Action<Context> disturb)
    {
        using Context context = new();
        disturb(context);
        ChcEncoder integers = new(context, fixture.Old, fixture.New, ChcArithmetic.Integers, []);

        ChcEncoder.ChcAnswer answer = integers.Query(overflows: false, Options with { TimeoutMs = 600_000 });

        return (answer.Status, answer.Spent, answer.Answer.ToString());
    }

    /// <summary>Terms nothing refers to once this returns, for a collection to free.</summary>
    private static void Unreferenced(Context context)
    {
        for (int i = 0; i < 200; i++)
        {
            context.MkAdd(context.MkIntConst("freed" + i.ToString(CultureInfo.InvariantCulture)), context.MkInt(i));
        }
    }
}
