using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// The encoding of every operation agrees with <see cref="IrInterpreter"/> on edge values (IrInterpreter's
/// doc: its semantics must match the Z3 encoding exactly). Old applies the operation to constant operands
/// and passes each result to a <c>Sink</c> call, so the trace carries every result; new passes the results
/// the interpreter computed. Equivalent means Z3 agrees on all of them; changing one result must diverge.
/// </summary>
public sealed class OperationSemanticsTests
{
    private static readonly IrBitVec Bv8 = new(8);
    private static readonly IrBitVec Bv16 = new(16);
    private static readonly IrBool Bool = new();
    private static readonly ulong[] Edges8 = [0, 1, 2, 0x7F, 0x80, 0xFE, 0xFF];
    private static readonly ulong[] Edges16 = [0, 1, 0x7F, 0x80, 0xFF, 0x100, 0x7FFF, 0x8000, 0xFFFF];

    public static TheoryData<IrBinaryOp> BinaryOps => [.. Enum.GetValues<IrBinaryOp>()];

    public static TheoryData<IrOverflowOp> OverflowOps => [.. Enum.GetValues<IrOverflowOp>()];

    public static TheoryData<IrUnaryOp> UnaryOps => [.. Enum.GetValues<IrUnaryOp>()];

    public static TheoryData<IrBinaryOp> BoolOps => [IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Eq, IrBinaryOp.Ne];

    [Theory]
    [MemberData(nameof(BinaryOps))]
    public void BitVectorBinaryOperationAgreesWithTheInterpreter(IrBinaryOp op)
    {
        IrType result = op >= IrBinaryOp.Eq ? Bool : Bv8;
        AssertAgrees(
            [.. Edges8.SelectMany(static a => Edges8.Select(b => ((IrValue)new IrBitVecValue(8, a), (IrValue)new IrBitVecValue(8, b))))],
            (target, a, b) => new IrBinary(target, op, a, b),
            result);
    }

    [Theory]
    [MemberData(nameof(BoolOps))]
    public void BoolBinaryOperationAgreesWithTheInterpreter(IrBinaryOp op)
    {
        bool[] values = [false, true];
        AssertAgrees(
            [.. values.SelectMany(a => values.Select(b => ((IrValue)new IrBoolValue(a), (IrValue)new IrBoolValue(b))))],
            (target, a, b) => new IrBinary(target, op, a, b),
            Bool);
    }

    [Theory]
    [MemberData(nameof(OverflowOps))]
    public void OverflowTestAgreesWithTheInterpreter(IrOverflowOp op)
    {
        AssertAgrees(
            [.. Edges8.SelectMany(static a => Edges8.Select(b => ((IrValue)new IrBitVecValue(8, a), (IrValue)new IrBitVecValue(8, b))))],
            (target, a, b) => new IrOverflows(target, op, a, b),
            Bool);
    }

    [Theory]
    [MemberData(nameof(UnaryOps))]
    public void UnaryOperationAgreesWithTheInterpreter(IrUnaryOp op)
    {
        IrValue[] bits8 = [.. Edges8.Select(static v => new IrBitVecValue(8, v))];
        (IrValue[] operands, IrType result) = op switch
        {
            IrUnaryOp.BoolNot => (new IrValue[] { new IrBoolValue(false), new IrBoolValue(true) }, (IrType)Bool),
            IrUnaryOp.ZExt or IrUnaryOp.SExt => (bits8, Bv16),
            IrUnaryOp.Trunc => ([.. Edges16.Select(static v => new IrBitVecValue(16, v))], Bv8),
            _ => (bits8, Bv8),
        };

        AssertAgrees([.. operands.Select(static v => (v, v))], (target, a, _) => new IrUnary(target, op, a), result);
    }

    private static void AssertAgrees(ImmutableArray<(IrValue A, IrValue B)> operands, Func<IrVar, IrVar, IrVar, IrInstruction> operation, IrType result)
    {
        List<IrInstruction> body = [];
        for (int i = 0; i < operands.Length; i++)
        {
            IrVar a = new($"a{i}", operands[i].A.Type);
            IrVar b = new($"b{i}", operands[i].B.Type);
            IrVar r = new($"r{i}", result);
            body.Add(new IrConst(a, operands[i].A));
            body.Add(new IrConst(b, operands[i].B));
            body.Add(operation(r, a, b));
            body.Add(new IrCall(null, null, new CallIdentity("Sink"), [r]));
        }

        IrProcedure old = Procedure(body);
        ImmutableArray<IrValue> expected = [.. IrInterpreter.Run(old, new IrInputs([]), new NoAnswers(), 10_000).Trace.Select(static c => c.Arguments[0])];
        VerificationOptions options = new(3, 10_000, []);

        Verdict same = new Z3Backend().Verify(old, Constants(expected), options);
        Verdict changed = new Z3Backend().Verify(old, Constants(expected.SetItem(expected.Length - 1, Flip(expected[^1]))), options);

        Assert.IsType<Equivalent>(same);
        Assert.IsType<Divergent>(changed);
    }

    private static IrProcedure Constants(ImmutableArray<IrValue> values)
    {
        List<IrInstruction> body = [];
        for (int i = 0; i < values.Length; i++)
        {
            IrVar r = new($"r{i}", values[i].Type);
            body.Add(new IrConst(r, values[i]));
            body.Add(new IrCall(null, null, new CallIdentity("Sink"), [r]));
        }

        return Procedure(body);
    }

    private static IrProcedure Procedure(List<IrInstruction> body) =>
        new(new ProcedureIdentity("Ops::M()"), [], null, [new IrBlock(new IrBlockId(0), [.. body], new IrReturn(null, []))], new IrBlockId(0));

    private static IrValue Flip(IrValue value) => value is IrBoolValue b
        ? new IrBoolValue(!b.Value)
        : new IrBitVecValue(((IrBitVecValue)value).Width, ((IrBitVecValue)value).Bits ^ 1);

    private sealed class NoAnswers : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position) => new(null, false);
    }
}
