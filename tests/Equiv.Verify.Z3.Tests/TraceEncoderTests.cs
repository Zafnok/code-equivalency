using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using Xunit;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3.Tests;

/// <summary>The Z3 function names <see cref="TraceEncoder"/> mints for a callee, their caching, and the shape of a trace.</summary>
public sealed class TraceEncoderTests
{
    [Fact]
    public void RuntimeChangedFunctionsCarryASidePrefixedOwnerTag()
    {
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        CallIdentity callee = new("Svc::F(int)", RuntimeChanged: true);

        FuncDecl old = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));
        FuncDecl @new = encoder.ResultFunction(Side.New, callee, [], new IrBitVec(32));

        Assert.Equal("f:Svc::F(int)()->bv32:old", old.Name.ToString());
        Assert.Equal("f:Svc::F(int)()->bv32:new", @new.Name.ToString());
    }

    [Fact]
    public void TheSameCalleeAndSignatureReuseTheSameFunctionDeclaration()
    {
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        CallIdentity callee = new("Svc::F(int)");

        FuncDecl first = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));
        FuncDecl second = encoder.ResultFunction(Side.Old, callee, [], new IrBitVec(32));

        Assert.Same(first, second);
    }

    /// <summary>
    /// Ticket P2-121: Z3 recurses natively on the depth of a concatenation, and one of 10,000 operands overflowed the
    /// stack, which no test survives. A trace of 20,000 blocks is the same sequence, no deeper than one concatenation
    /// of <see cref="TraceEncoder.MaxOperands"/> and the halvings above it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(TraceEncoder.MaxOperands - 1)]
    [InlineData(TraceEncoder.MaxOperands)]
    [InlineData(20_000)]
    public void ATraceHoldsItsBlocksInOrderAndStaysShallowHoweverManyThereAre(int blocks)
    {
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        BoolExpr[] reach = [.. Enumerable.Range(0, blocks).Select(i => context.MkBoolConst("reach." + i.ToString(CultureInfo.InvariantCulture)))];

        SeqExpr trace = encoder.Trace(reach.Select((r, i) => (r, (IReadOnlyList<Expr>)[Event(context, encoder, i)])));

        (List<Expr> operands, int depth) = Operands(trace);
        Assert.Equal(blocks + 1, operands.Count);
        Assert.Equal(0, (int)operands[0].NumArgs);
        Assert.All(reach, (r, i) => Assert.Equal(r, operands[i + 1].Arg(0)));
        Assert.InRange(depth, 0, TraceEncoder.MaxOperands + 8);
    }

    [Fact]
    public void ABlockHoldsItsEventsInOrderAndStaysShallowHoweverManyThereAre()
    {
        const int Events = 20_000;
        using Context context = new();
        TraceEncoder encoder = new(new SortMapper(context), [], [], []);
        Expr[] events = [.. Enumerable.Range(0, Events).Select(i => Event(context, encoder, i))];

        SeqExpr trace = encoder.Trace([(context.MkBoolConst("reach"), events)]);

        (List<Expr> blocks, _) = Operands(trace);
        (List<Expr> units, int depth) = Operands(blocks[1].Arg(1));
        Assert.Equal(events, units.Select(static u => u.Arg(0)));
        Assert.InRange(depth, 0, TraceEncoder.MaxOperands + 8);
    }

    /// <summary>The event of a call to <c>Svc::F()</c> at <paramref name="position"/>.</summary>
    private static Expr Event(Context context, TraceEncoder encoder, int position) =>
        encoder.Call(Side.Old, new IrCall(Target: null, Threw: null, new CallIdentity("Svc::F()"), []), [], context.MkBV(position, 32), []).Event;

    /// <summary>
    /// The operands of a concatenation, left to right, and how deep the concatenations above them nest; walked without
    /// recursion, since the depth is what is under test.
    /// </summary>
    private static (List<Expr> Operands, int Depth) Operands(Expr concatenation)
    {
        List<Expr> operands = [];
        int deepest = 0;
        Stack<(Expr Term, int Depth)> pending = new([(concatenation, 0)]);
        while (pending.TryPop(out (Expr Term, int Depth) next))
        {
            if (next.Term.IsApp && next.Term.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_SEQ_CONCAT)
            {
                foreach (Expr operand in next.Term.Args.Reverse())
                {
                    pending.Push((operand, next.Depth + 1));
                }
            }
            else
            {
                operands.Add(next.Term);
                deepest = Math.Max(deepest, next.Depth);
            }
        }

        return (operands, deepest);
    }
}
