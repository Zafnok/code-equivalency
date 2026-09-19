using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary><see cref="SsaBuilder"/> on hand-built draft graphs, independent of Roslyn.</summary>
public sealed class SsaBuilderTests
{
    private static readonly IrBitVec Bv32 = new(32);
    private static readonly SourceSpan Span = new("x.cs", 1, 1, 1, 2);

    [Fact]
    public void DiamondJoinGetsAPhiNamedAfterTheVariable()
    {
        SsaBuilder ssa = new();
        IrVar c = new("c", new IrBool(), "c");
        IrVar a = new("a", Bv32, "a");
        SsaBuilder.Variable x = new(new IrVar("x", Bv32, "x"));
        IrBlockId entry = ssa.NewBlock(), then = ssa.NewBlock(), otherwise = ssa.NewBlock(), join = ssa.NewBlock();
        ssa.Store(entry, x, a);
        ssa.Terminate(entry, new IrBranch(c, then, otherwise));
        IrVar one = ssa.Temp(Bv32);
        ssa.Emit(then, new IrConst(one, new IrBitVecValue(32, 1)));
        ssa.Store(then, x, one);
        ssa.Terminate(then, new IrGoto(join));
        ssa.Terminate(otherwise, new IrGoto(join));
        IrVar result = ssa.Load(join, x);
        ssa.Terminate(join, new IrReturn(result, []));

        ImmutableArray<IrBlock> blocks = ssa.Build(entry, [], Span);

        IrPhi phi = Assert.IsType<IrPhi>(Assert.Single(blocks[3].Instructions));
        Assert.Equal("x", phi.Target.SourceName);
        Assert.Equal([(then, blocks[1].Instructions.OfType<IrConst>().Single().Target), (otherwise, a)], phi.Incoming.OrderBy(static i => i.From.Value));
        Assert.Equal(new IrReturn(phi.Target, []), blocks[3].Terminator);
        Assert.StartsWith("x.", blocks[1].Instructions.OfType<IrConst>().Single().Target.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopHeaderPhiIsSealedAfterTheLatchAndUnchangedVariablesHaveNone()
    {
        SsaBuilder ssa = new();
        IrVar c = new("c", new IrBool(), "c");
        IrVar a = new("a", Bv32, "a");
        SsaBuilder.Variable x = new(new IrVar("x", Bv32, "x"));
        SsaBuilder.Variable y = new(new IrVar("y", Bv32, "y"));
        IrBlockId entry = ssa.NewBlock(), header = ssa.NewBlock(), body = ssa.NewBlock(), exit = ssa.NewBlock();
        ssa.Store(entry, x, a);
        ssa.Store(entry, y, a);
        ssa.Terminate(entry, new IrGoto(header));
        ssa.Terminate(header, new IrBranch(c, body, exit));
        IrVar next = ssa.Temp(Bv32);
        ssa.Emit(body, new IrBinary(next, IrBinaryOp.Add, ssa.Load(body, x), ssa.Load(body, y)));
        ssa.Store(body, x, next);
        ssa.Terminate(body, new IrGoto(header));
        ssa.Terminate(exit, new IrReturn(ssa.Load(exit, x), []));

        ImmutableArray<IrBlock> blocks = ssa.Build(entry, [], Span);

        IrPhi phi = Assert.IsType<IrPhi>(Assert.Single(blocks[1].Instructions));
        Assert.Equal("x", phi.Target.SourceName);
        IrBinary add = Assert.IsType<IrBinary>(Assert.Single(blocks[2].Instructions));
        Assert.Equal(phi.Target, add.A);
        Assert.Equal(a, add.B);
        Assert.Empty(IrValidator.Validate(new IrProcedure(new ProcedureIdentity("P"), [new(c, IrParameterKind.In), new(a, IrParameterKind.In)], Bv32, blocks, entry)));
    }

    [Fact]
    public void ExitsNameTheLiveValueOfEveryOutAndUnreachableBlocksAreDropped()
    {
        SsaBuilder ssa = new();
        IrVar r = new("r", Bv32, "r");
        SsaBuilder.Variable variable = new(r);
        IrBlockId entry = ssa.NewBlock(), dead = ssa.NewBlock();
        ssa.Store(entry, variable, r);
        IrVar two = ssa.Temp(Bv32);
        ssa.Emit(entry, new IrConst(two, new IrBitVecValue(32, 2)));
        ssa.Store(entry, variable, two);
        ssa.Terminate(entry, new IrThrow("E", []));
        ssa.Terminate(dead, new IrReturn(null, []));

        ImmutableArray<IrBlock> blocks = ssa.Build(entry, [(variable, r)], Span);

        IrBlock block = Assert.Single(blocks);
        IrOut @out = Assert.Single(Assert.IsType<IrThrow>(block.Terminator).Outs);
        Assert.Equal(r, @out.Param);
        Assert.Equal(block.Instructions.OfType<IrConst>().Single().Target, @out.Final);
    }
}
