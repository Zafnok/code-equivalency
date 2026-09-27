using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-001: <see cref="ChcEncoder.Solves"/> on answers that define no relation, whatever Spacer's own answers
/// happen to leave out. Each relation then reads as false, and as true once a rule concludes it from premises that hold.
/// </summary>
public sealed class ChcEncoderTests
{
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

        Assert.True(wrapping.Solves(context.MkTrue(), 10_000));
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

        Assert.False(wrapping.Solves(context.MkTrue(), 10_000));
    }
}
