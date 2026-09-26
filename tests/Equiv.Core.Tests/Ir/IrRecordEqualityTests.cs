using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>Records holding arrays compare element-wise, so separately built equal values are equal.</summary>
public sealed class IrRecordEqualityTests
{
    private static readonly IrBitVec Bv32 = new(32);
    private static readonly IrVar A = new("a", Bv32);
    private static readonly IrVar B = new("b", Bv32);
    private static readonly IrValue One = new IrBitVecValue(32, 1);

    [Fact]
    public void Procedure() =>
        AssertStructural(
            () => new IrProcedure(new ProcedureIdentity("P"), [new IrParameter(A, IrParameterKind.In)], Bv32, [BuildBlock()], new IrBlockId(0)),
            p => p with { Blocks = [] });

    [Fact]
    public void BlockEquality() => AssertStructural(BuildBlock, b => b with { Instructions = [] });

    [Fact]
    public void Phi() => AssertStructural(() => new IrPhi(A, [(new IrBlockId(0), B)]), p => p with { Incoming = [] });

    [Fact]
    public void Call() => AssertStructural(() => new IrCall(A, Threw: null, new CallIdentity("F"), [B]), c => c with { Args = [] });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Pure(int field) =>
        AssertStructural(
            () => new IrPure(A, [new IrPureThrow(new IrVar("f", new IrBool()), "E")], "dec.add", [A, B]),
            p => field switch
            {
                0 => p with { Target = B },
                1 => p with { Throws = [] },
                2 => p with { Function = "dec.sub" },
                3 => p with { Args = [B, A] },
                _ => p with { RuntimeSensitive = true },
            });

    [Fact]
    public void PureResult() => AssertStructural(() => new IrPureResult(One, [true, false]), r => r with { Threw = [true] });

    [Fact]
    public void CallHeap() =>
        AssertStructural(() => new IrCall(A, Threw: null, new CallIdentity("F"), [B]) { Heap = [new IrHeapPair("m", A, B)] }, c => c with { Heap = [] });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Opaque(int field) =>
        AssertStructural(
            () => new IrOpaque(A, "Lambda", new SourceSpan("a.cs", 1, 2, 3, 4)) { Fingerprint = "f", Reads = [A, B], Threw = new IrVar("t", new IrBool()), Heap = [new IrHeapPair("m", A, B)] },
            o => field switch
            {
                0 => o with { Target = B },
                1 => o with { Reason = "Other" },
                2 => o with { Span = new SourceSpan("b.cs", 1, 2, 3, 4) },
                3 => o with { WholeBody = true },
                4 => o with { Fingerprint = null },
                5 => o with { Reads = [B, A] },
                6 => o with { Threw = null },
                _ => o with { Heap = [] },
            });

    [Fact]
    public void CallResult() => AssertStructural(() => new IrCallResult(One, Threw: false) { Heap = [One] }, r => r with { Heap = [] });

    [Fact]
    public void Switch() =>
        AssertStructural(() => new IrSwitch(A, [(One, new IrBlockId(1))], new IrBlockId(2)), s => s with { Cases = [] });

    [Fact]
    public void Return() => AssertStructural(() => new IrReturn(A, [new IrOut(B, A)]), r => r with { Outs = [] });

    [Fact]
    public void Throw() => AssertStructural(() => new IrThrow("E", [new IrOut(B, A)]), t => t with { ExceptionType = "F" });

    [Fact]
    public void Inputs() => AssertStructural(() => new IrInputs([One]), i => i with { Arguments = [] });

    [Fact]
    public void CallRecord() => AssertStructural(() => new IrCallRecord(new CallIdentity("F"), [One]), r => r with { Arguments = [] });

    [Fact]
    public void CallRecordHeap() =>
        AssertStructural(() => new IrCallRecord(new CallIdentity("F"), [One]) { Heap = [new IrHeapSlice("m", One)] }, r => r with { Heap = [] });

    [Fact]
    public void Run() =>
        AssertStructural(
            () => new IrRun(new IrReturned(One), [One], [new IrCallRecord(new CallIdentity("F"), [One])]),
            r => r with { Trace = [] });

    private static IrBlock BuildBlock() =>
        new(new IrBlockId(0), [new IrConst(A, One), new IrBinary(B, IrBinaryOp.Add, A, A)], new IrReturn(B, []));

    private static void AssertStructural<T>(Func<T> build, Func<T, T> change)
        where T : class, IEquatable<T>
    {
        T first = build();
        T second = build();
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(change(first)));
        Assert.False(first.Equals(other: null));
    }
}
