using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

using static VerifyXunit.Verifier;

namespace Equiv.Core.Tests.Ir;

/// <summary>Pins <see cref="IrText.Dump"/> on three hand-built procedures, shaped like what the frontend will produce.</summary>
public sealed class IrTextSnapshotTests
{
    private static readonly IrBitVec Bv32 = new(32);
    private static readonly IrBool Bool = new();
    private static readonly ImmutableArray<IrOut> NoOuts = [];

    /// <summary><c>static int AddChecked(int a, ref int total) { total = checked(total + a); return total; }</c></summary>
    [Fact]
    public Task CheckedAddWithRefParameter()
    {
        IrVar a = new("a", Bv32, "a");
        IrVar total = new("total", Bv32, "total");
        IrVar overflow = new("t0", Bool);
        IrVar sum = new("total.1", Bv32, "total");
        IrProcedure p = new(
            new ProcedureIdentity("Samples.Math::AddChecked(int32,ref int32)"),
            [new IrParameter(a, IrParameterKind.In), new IrParameter(total, IrParameterKind.Ref)],
            Bv32,
            [
                new IrBlock(new IrBlockId(0), [new IrOverflows(overflow, IrOverflowOp.SAdd, total, a)], new IrBranch(overflow, new IrBlockId(1), new IrBlockId(2))),
                new IrBlock(new IrBlockId(1), [], new IrThrow("System.OverflowException", [new IrOut(total, total)])),
                new IrBlock(new IrBlockId(2), [new IrBinary(sum, IrBinaryOp.Add, total, a)], new IrReturn(sum, [new IrOut(total, sum)])),
            ],
            new IrBlockId(0));

        return Verify(Checked(p));
    }

    /// <summary><c>static uint Sum(uint n) { uint s = 0; for (uint i = 0; i &lt; n; i++) s += i; return s; }</c></summary>
    [Fact]
    public Task CountingLoop()
    {
        IrVar n = new("n", Bv32, "n");
        IrVar zero = new("t0", Bv32);
        IrVar one = new("t1", Bv32);
        IrVar s = new("s.1", Bv32, "s");
        IrVar i = new("i.1", Bv32, "i");
        IrVar more = new("t2", Bool);
        IrVar s2 = new("s.2", Bv32, "s");
        IrVar i2 = new("i.2", Bv32, "i");
        IrProcedure p = new(
            new ProcedureIdentity("Samples.Math::Sum(uint32)"),
            [new IrParameter(n, IrParameterKind.In)],
            Bv32,
            [
                new IrBlock(new IrBlockId(0), [new IrConst(zero, new IrBitVecValue(32, 0)), new IrConst(one, new IrBitVecValue(32, 1))], new IrGoto(new IrBlockId(1))),
                new IrBlock(
                    new IrBlockId(1),
                    [
                        new IrPhi(s, [(new IrBlockId(0), zero), (new IrBlockId(2), s2)]),
                        new IrPhi(i, [(new IrBlockId(0), zero), (new IrBlockId(2), i2)]),
                        new IrBinary(more, IrBinaryOp.Ult, i, n),
                    ],
                    new IrBranch(more, new IrBlockId(2), new IrBlockId(3))),
                new IrBlock(new IrBlockId(2), [new IrBinary(s2, IrBinaryOp.Add, s, i), new IrBinary(i2, IrBinaryOp.Add, i, one)], new IrGoto(new IrBlockId(1))),
                new IrBlock(new IrBlockId(3), [], new IrReturn(s, NoOuts)),
            ],
            new IrBlockId(0));

        return Verify(Checked(p));
    }

    /// <summary>
    /// A field write through a nullable object, a switch, a call that may throw, and an
    /// unsupported construct: <c>void Store(Order o, int kind) { o.Count = kind switch { 1 =&gt; Svc.Next(kind), _ =&gt; 0 }; lock (o) { } }</c>
    /// </summary>
    [Fact]
    public Task HeapSwitchCallAndOpaque()
    {
        IrSort order = new("Samples.Order");
        IrMap countField = new(order, Bv32);
        IrVar o = new("o", order, "o");
        IrVar oIsNull = new("o.isnull", Bool, "o");
        IrVar kind = new("kind", Bv32, "kind");
        IrVar count = new("Order.Count", countField);
        IrVar zero = new("t0", Bv32);
        IrVar next = new("t1", Bv32);
        IrVar threw = new("t2", Bool);
        IrVar value = new("t3", Bv32);
        IrVar count2 = new("Order.Count.1", countField);
        IrProcedure p = new(
            new ProcedureIdentity("Samples.Orders::Store(Samples.Order,int32)"),
            [
                new IrParameter(o, IrParameterKind.In),
                new IrParameter(oIsNull, IrParameterKind.In),
                new IrParameter(kind, IrParameterKind.In),
                new IrParameter(count, IrParameterKind.Ref),
            ],
            null,
            [
                new IrBlock(new IrBlockId(0), [new IrConst(zero, new IrBitVecValue(32, 0))], new IrSwitch(kind, [(new IrBitVecValue(32, 1), new IrBlockId(1))], new IrBlockId(3))),
                new IrBlock(new IrBlockId(1), [new IrCall(next, threw, new CallIdentity("Samples.Svc::Next(int32)"), [kind])], new IrBranch(threw, new IrBlockId(2), new IrBlockId(3))),
                new IrBlock(new IrBlockId(2), [], new IrThrow("System.Exception", [new IrOut(count, count)])),
                new IrBlock(
                    new IrBlockId(3),
                    [new IrPhi(value, [(new IrBlockId(0), zero), (new IrBlockId(1), next)])],
                    new IrBranch(oIsNull, new IrBlockId(4), new IrBlockId(5))),
                new IrBlock(new IrBlockId(4), [], new IrThrow("System.NullReferenceException", [new IrOut(count, count)])),
                new IrBlock(
                    new IrBlockId(5),
                    [
                        new IrMapWrite(count2, count, o, value),
                        new IrOpaque(null, "lock statement", new SourceSpan("Samples/Orders.cs", 12, 9, 12, 20)),
                    ],
                    new IrReturn(null, [new IrOut(count, count2)])),
            ],
            new IrBlockId(0));

        return Verify(Checked(p));
    }

    private static string Checked(IrProcedure procedure)
    {
        Assert.Empty(IrValidator.Validate(procedure));
        string text = IrText.Dump(procedure);
        Assert.Equal(procedure, IrText.Parse(text));
        return text;
    }
}
