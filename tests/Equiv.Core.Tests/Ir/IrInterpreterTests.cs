using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

public sealed class IrInterpreterTests
{
    private const string Header = "proc \"T::M\" ";

    private static readonly ICallOracle Answers42 = new ScriptedOracle(static (_, _, type) => new IrCallResult(type is null ? null : Bv(42), Threw: false));

    [Fact]
    public void ConstBinaryBranchAndReturn()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %t0: bv32 = const bv32 1
              %t1: bv32 = add %a, %t0
              %c: bool = slt %t1, %b
              br %c, B1, B2
            B1:
              ret %t1
            B2:
              throw "System.InvalidOperationException"
            """);

        Assert.Equal(new IrReturned(Bv(3)), Run(p, Bv(2), Bv(10)).Outcome);
        Assert.Equal(new IrThrew("System.InvalidOperationException"), Run(p, Bv(20), Bv(10)).Outcome);
    }

    [Fact]
    public void OverflowsAndUnaryInstructions()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv8) -> bool entry B0
            B0:
              %n: bv8 = neg %a
              %w: bv32 = sext %n
              %o: bool = overflows sadd %a, %a
              %r: bool = boolnot %o
              ret %r
            """);

        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), Run(p, new IrBitVecValue(8, 0x7F)).Outcome);
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), Run(p, new IrBitVecValue(8, 1)).Outcome);
    }

    [Fact]
    public void PhisOnALoopHeaderReadTheirOperandsInParallel()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%x: bv32, %y: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              goto B1
            B1:
              %p: bv32 = phi [B0: %x, B2: %q]
              %q: bv32 = phi [B0: %y, B2: %p]
              %i: bv32 = phi [B0: %zero, B2: %next]
              %more: bool = ult %i, %one
              br %more, B2, B3
            B2:
              %next: bv32 = add %i, %one
              goto B1
            B3:
              %d: bv32 = sub %p, %q
              ret %d
            """);

        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, -2)), Run(p, Bv(5), Bv(3)).Outcome);
    }

    [Fact]
    public void SwitchTakesTheFirstMatchingCaseElseTheDefault()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              switch %a [bv32 1 -> B1, bv32 1 -> B2, bv32 2 -> B2] default B3
            B1:
              %r1: bv32 = const bv32 10
              ret %r1
            B2:
              %r2: bv32 = const bv32 20
              ret %r2
            B3:
              %r3: bv32 = const bv32 30
              ret %r3
            """);

        Assert.Equal(new IrReturned(Bv(10)), Run(p, Bv(1)).Outcome);
        Assert.Equal(new IrReturned(Bv(20)), Run(p, Bv(2)).Outcome);
        Assert.Equal(new IrReturned(Bv(30)), Run(p, Bv(3)).Outcome);
    }

    [Fact]
    public void CallsAreRecordedInOrderAndAnswerTheirTargetsAndThrewFlags()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              call "Log"(%a)
              %r: bv32 = call "F"(%a, %a) threw %t: bool
              br %t, B1, B2
            B1:
              throw "System.Exception"
            B2:
              ret %r
            """);
        ScriptedOracle throwing = new(static (callee, _, type) =>
            new IrCallResult(type is null ? null : Bv(7), string.Equals(callee.Value, "F", StringComparison.Ordinal)));

        IrRun normal = Run(p, Answers42, Bv(1));
        IrRun threw = IrInterpreter.Run(p, new IrInputs([Bv(1)]), throwing, 100);

        Assert.Equal(new IrReturned(Bv(42)), normal.Outcome);
        Assert.Equal(
            [new IrCallRecord(new CallIdentity("Log"), [Bv(1)]), new IrCallRecord(new CallIdentity("F"), [Bv(1), Bv(1)])],
            normal.Trace);
        Assert.Equal(new IrThrew("System.Exception"), threw.Outcome);
        Assert.Equal([0, 1], throwing.Positions);
    }

    [Fact]
    public void AnOracleAnswerOfTheWrongTypeIsAnError()
    {
        IrProcedure p = IrText.Parse(Header + """
            () -> bv32 entry B0
            B0:
              %r: bv32 = call "F"()
              ret %r
            """);

        Assert.Throws<InvalidOperationException>(() => IrInterpreter.Run(p, new IrInputs([]), new ScriptedOracle(static (_, _, _) => new IrCallResult(new IrBoolValue(Value: true), Threw: false)), 10));
        Assert.Throws<InvalidOperationException>(() => IrInterpreter.Run(p, new IrInputs([]), new ScriptedOracle(static (_, _, _) => new IrCallResult(Value: null, Threw: false)), 10));
    }

    private const string HeapCall = """
        (%k: bv32, ref %field.C.x: map<bv32, bv32>) -> bv32 entry B0
        B0:
          %v: bv32 = const bv32 5
          %x1: map<bv32, bv32> = mapwrite %field.C.x, %k, %v
          call "Bump"(%k) heap("field.C.x" %x1 -> %x2: map<bv32, bv32>)
          %r: bv32 = mapread %x2, %k
          ret %r outs(%field.C.x = %x2)
        """;

    [Fact]
    public void ACallGetsTheHeapItReadsAndItsAnswerIsTheNewVersion()
    {
        IrProcedure p = IrText.Parse(Header + HeapCall);
        IrMapValue empty = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);
        IrMapValue written = empty.Write(Bv(1), Bv(5));
        IrMapValue bumped = written.Write(Bv(1), Bv(6));
        HeapOracle oracle = new([bumped]);

        IrRun run = Run(p, oracle, Bv(1), empty);

        Assert.Equal(new IrReturned(Bv(6)), run.Outcome);
        Assert.Equal([bumped], run.Outs);
        Assert.Equal([new IrHeapSlice("field.C.x", written)], oracle.Heap);
        Assert.Equal([new IrCallRecord(new CallIdentity("Bump"), [Bv(1)]) { Heap = [new IrHeapSlice("field.C.x", written)] }], run.Trace);
    }

    [Fact]
    public void AnOracleThatAnswersNoHeapLeavesEveryMapUnchanged()
    {
        IrProcedure p = IrText.Parse(Header + HeapCall);
        IrMapValue empty = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

        IrRun run = Run(p, Answers42, Bv(1), empty);

        Assert.Equal(new IrReturned(Bv(5)), run.Outcome);
        Assert.Equal([empty.Write(Bv(1), Bv(5))], run.Outs);
    }

    [Fact]
    public void AnOracleHeapThatDoesNotFitTheSlicesIsAnError()
    {
        IrProcedure p = IrText.Parse(Header + HeapCall);
        IrMapValue empty = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

        Assert.Throws<InvalidOperationException>(() => Run(p, new HeapOracle([empty, empty]), Bv(1), empty));
        Assert.Throws<InvalidOperationException>(() => Run(p, new HeapOracle([Bv(1)]), Bv(1), empty));
    }

    private const string RefOutCall = """
        (%n: bv32) -> bv32 entry B0
        B0:
          %ok: bool = call "TryParse"(%n) refout(%n.1: bv32, %b: bool)
          br %b, B1, B2
        B1:
          ret %n.1
        B2:
          ret %n
        """;

    [Fact]
    public void ACallsRefOutputsAreItsAnswers()
    {
        IrProcedure p = IrText.Parse(Header + RefOutCall);
        ScriptedOracle oracle = new((_, _, _) => new IrCallResult(new IrBoolValue(Value: true), Threw: false) { RefOuts = [Bv(9), new IrBoolValue(Value: true)] });

        Assert.Equal(new IrReturned(Bv(9)), Run(p, oracle, Bv(1)).Outcome);
        Assert.Equal([(ImmutableArray<IrType>)[new IrBitVec(32), new IrBool()]], oracle.RefOuts);
    }

    [Fact]
    public void RefOutputsThatDoNotFitTheCallAreAnError()
    {
        IrProcedure p = IrText.Parse(Header + RefOutCall);

        Assert.Throws<InvalidOperationException>(() => Run(p, Answers42, Bv(1)));
        Assert.Throws<InvalidOperationException>(() => Run(p, new ScriptedOracle(static (_, _, _) => new IrCallResult(new IrBoolValue(Value: true), Threw: false) { RefOuts = [Bv(9), Bv(1)] }), Bv(1)));
    }

    [Fact]
    public void MapsReadWhatWasWritten()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%m: map<bv32, bv32>, %k: bv32) -> bv32 entry B0
            B0:
              %v: bv32 = const bv32 9
              %m2: map<bv32, bv32> = mapwrite %m, %k, %v
              %r: bv32 = mapread %m2, %k
              ret %r
            """);
        IrMapValue empty = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

        Assert.Equal(new IrReturned(Bv(9)), Run(p, empty, Bv(4)).Outcome);
    }

    [Fact]
    public void ReachingAnOpaqueStopsTheRunButKeepsTheTrace()
    {
        IrProcedure p = IrText.Parse(Header + """
            () entry B0
            B0:
              call "F"()
              opaque "dynamic" at "a.cs" 1:2-3:4
              ret
            """);

        IrRun run = Run(p, Answers42);

        Assert.Equal(new IrOpaqueReached("dynamic", new SourceSpan("a.cs", 1, 2, 3, 4)), run.Outcome);
        Assert.Single(run.Trace);
        Assert.Empty(run.Outs);
    }

    [Fact]
    public void ReachingUnreachableIsInfeasible()
    {
        IrProcedure p = IrText.Parse(Header + """
            () entry B0
            B0:
              unreachable
            """);

        Assert.Equal(new IrInfeasible(), Run(p).Outcome);
    }

    [Fact]
    public void ReturnWithoutValue()
    {
        IrProcedure p = IrText.Parse(Header + """
            () entry B0
            B0:
              ret
            """);

        Assert.Equal(new IrReturned(Value: null), Run(p).Outcome);
    }

    [Fact]
    public void ByRefFinalValuesAreReportedOnReturnAndOnThrow()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%c: bool, ref %r: bv32, out %o: bv32) entry B0
            B0:
              %r1: bv32 = const bv32 5
              br %c, B1, B2
            B1:
              ret outs(%r = %r1, %o = %r1)
            B2:
              throw "E" outs(%r = %r, %o = %r1)
            """);

        IrRun returned = Run(p, new IrBoolValue(Value: true), Bv(1), Bv(2));
        IrRun threw = Run(p, new IrBoolValue(Value: false), Bv(1), Bv(2));

        Assert.Equal(new IrRun(new IrReturned(Value: null), [Bv(5), Bv(5)], []), returned);
        Assert.Equal(new IrRun(new IrThrew("E"), [Bv(1), Bv(5)], []), threw);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void TheStepBudgetCountsInstructionsAndTerminators(int budget)
    {
        IrProcedure p = IrText.Parse(Header + """
            () -> bv32 entry B0
            B0:
              %a: bv32 = const bv32 1
              %b: bv32 = const bv32 2
              ret %b
            """);

        Assert.Equal(new IrBudgetExhausted(), IrInterpreter.Run(p, new IrInputs([]), Answers42, budget).Outcome);
        Assert.Equal(new IrReturned(Bv(2)), IrInterpreter.Run(p, new IrInputs([]), Answers42, 3).Outcome);
    }

    [Fact]
    public void RejectsInvalidProceduresAndMismatchedInputs()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) entry B0
            B0:
              ret
            """);
        IrProcedure invalid = p with { Entry = new IrBlockId(7) };

        Assert.Throws<ArgumentException>(() => IrInterpreter.Run(invalid, new IrInputs([Bv(1)]), Answers42, 10));
        Assert.Throws<ArgumentException>(() => IrInterpreter.Run(p, new IrInputs([new IrBoolValue(Value: true)]), Answers42, 10));
        Assert.Throws<ArgumentException>(() => IrInterpreter.Run(p, new IrInputs([]), Answers42, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => IrInterpreter.Run(p, new IrInputs([Bv(1)]), Answers42, -1));
    }

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static IrRun Run(IrProcedure procedure, params IrValue[] args) => Run(procedure, Answers42, args);

    private static IrRun Run(IrProcedure procedure, ICallOracle oracle, params IrValue[] args) =>
        IrInterpreter.Run(procedure, new IrInputs([.. args]), oracle, 1000);

    /// <summary>Answers every call with no value and <paramref name="answer"/> as its heap, remembering the heap it was given.</summary>
    private sealed class HeapOracle(ImmutableArray<IrValue> answer) : ICallOracle
    {
        public ImmutableArray<IrHeapSlice> Heap { get; private set; } = [];

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Heap = heap;
            return new IrCallResult(Value: null, Threw: false) { Heap = answer };
        }
    }

    private sealed class ScriptedOracle(Func<CallIdentity, ImmutableArray<IrValue>, IrType?, IrCallResult> answer) : ICallOracle
    {
        public List<int> Positions { get; } = [];

        public List<ImmutableArray<IrType>> RefOuts { get; } = [];

        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Positions.Add(position);
            RefOuts.Add(refOuts);
            return answer(callee, arguments, resultType);
        }
    }
}
