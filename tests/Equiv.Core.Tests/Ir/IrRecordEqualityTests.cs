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
    public void Call() => AssertStructural(() => new IrCall(A, null, new CallIdentity("F"), [B]), c => c with { Args = [] });

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
    public void Run() =>
        AssertStructural(
            () => new IrRun(new IrReturned(One), [One], [new IrCallRecord(new CallIdentity("F"), [One])]),
            r => r with { Trace = [] });

    private static IrBlock BuildBlock() =>
        new(new IrBlockId(0), [new IrConst(A, One), new IrBinary(B, IrBinaryOp.Add, A, A)], new IrReturn(B, ImmutableArray<IrOut>.Empty));

    private static void AssertStructural<T>(Func<T> build, Func<T, T> change)
        where T : class, IEquatable<T>
    {
        T first = build();
        T second = build();
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(change(first)));
        Assert.False(first.Equals(null));
    }
}
