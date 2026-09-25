using System.Collections.Immutable;

using Equiv.Core.Ir;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// <see cref="IrPure"/> in Core (ADR 0025; ticket M4-002 criteria 1 and 2): its validator rule, its runtime-sensitive
/// flag in the text format, and how <see cref="IrInterpreter"/> evaluates it through an <see cref="IPureOracle"/> and
/// taints it.
/// </summary>
public sealed class IrPureTests
{
    private const string Header = "proc \"T::M\" ";

    private static readonly IrProcedure Divide = IrText.Parse(Header + """
        (%a: sort "System.Decimal", %b: sort "System.Decimal") -> sort "System.Decimal" entry B0
        B0:
          %q: sort "System.Decimal" = pure "dec.div"(%a, %b) throws(%z: bool "System.DivideByZeroException", %o: bool "System.OverflowException")
          br %z, B1, B2
        B1:
          throw "System.DivideByZeroException"
        B2:
          br %o, B3, B4
        B3:
          throw "System.OverflowException"
        B4:
          ret %q
        """);

    [Fact]
    public void PureAddsNoTraceEvent()
    {
        IrRun run = Run(Divide, new Answers(Element(9), false, false));

        Assert.Equal(new IrReturned(Element(9)), run.Outcome);
        Assert.Empty(run.Trace);
    }

    [Theory]
    [InlineData(true, false, "System.DivideByZeroException")]
    [InlineData(false, true, "System.OverflowException")]
    [InlineData(true, true, "System.DivideByZeroException")]
    public void EachFlagBranchesToItsOwnException(bool divideByZero, bool overflow, string thrown)
    {
        Assert.Equal(new IrThrew(thrown), Run(Divide, new Answers(Element(9), divideByZero, overflow)).Outcome);
    }

    [Fact]
    public void TheOracleIsAskedWithTheInstructionAndItsArguments()
    {
        Answers answers = new(Element(9), false, false);

        Run(Divide, answers);

        (IrPure pure, ImmutableArray<IrValue> arguments) = Assert.Single(answers.Asked);
        Assert.Equal("dec.div", pure.Function);
        Assert.Equal<IrValue>([Element(1), Element(2)], arguments);
    }

    [Fact]
    public void PureResultsAndFlagsAreTaintedWhenReplayTracksTaint()
    {
        IrRun returned = Run(Divide, new Answers(Element(9), false, false), static _ => false);
        IrRun thrown = Run(Divide, new Answers(Element(9), true, false), static _ => false);

        Assert.Equal(new IrTaint(Outcome: true, Value: true, [], [], [new CallIdentity("dec.div")]), returned.Taint);
        Assert.Equal(new IrTaint(Outcome: true, Value: true, [], [], [new CallIdentity("dec.div")]), thrown.Taint);
    }

    [Fact]
    public void APureValueTaintsOnlyWhatItFeeds()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: sort "System.Double", %b: bv32) -> bv32 entry B0
            B0:
              %s: sort "System.Double" = pure "f64.add"(%a, %a)
              %t: sort "System.Double" = pure "f64.add"(%s, %a)
              call "log"(%t)
              call "log"(%b)
              ret %b
            """);

        IrRun run = Run(p, [new IrSortValue("System.Double", 1), new IrBitVecValue(32, 3)], new Answers(new IrSortValue("System.Double", 5)), static _ => false);

        Assert.Equal(new IrTaint(Outcome: false, Value: false, [], [0], [new CallIdentity("f64.add")]), run.Taint);
    }

    [Fact]
    public void WithoutATaintPredicatePureIsUntainted()
    {
        Assert.Equal(IrTaint.None, Run(Divide, new Answers(Element(9), false, false)).Taint);
    }

    [Fact]
    public void ARunThatReachesPureWithoutAnOracleFails()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            IrInterpreter.Run(Divide, Inputs(), new NoCalls(), 100));

        Assert.Contains("dec.div", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerOfTheWrongShapeFails(bool wrongType)
    {
        Answers answers = wrongType ? new Answers(new IrBitVecValue(32, 1), false, false) : new Answers(Element(9), false);

        Assert.Throws<InvalidOperationException>(() => Run(Divide, answers));
    }

    [Fact]
    public void AFlagMustBeBool()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              %q: bv32 = pure "f"(%a) throws(%z: bv32 "E")
              ret %q
            """);

        IrDiagnostic diagnostic = Assert.Single(IrValidator.Validate(p));
        Assert.Equal(IrDiagnosticIds.OperandTypes, diagnostic.Id);
    }

    [Fact]
    public void TheRuntimeSensitiveFlagRoundTripsInIrText()
    {
        IrPure flagged = new(new IrVar("q", new IrBitVec(32)), [], "conv.f64.i32", [new IrVar("a", new IrSort("System.Double"))]) { RuntimeSensitive = true };
        IrProcedure p = new(
            new ProcedureIdentity("P"),
            [new IrParameter(flagged.Args[0], IrParameterKind.In)],
            new IrBitVec(32),
            [new IrBlock(new IrBlockId(0), [flagged], new IrReturn(flagged.Target, []))],
            new IrBlockId(0));

        string dumped = IrText.Dump(p);

        Assert.Contains("= pure \"conv.f64.i32\"!(%a)\n", dumped, StringComparison.Ordinal);
        Assert.Equal(flagged, IrText.Parse(dumped).Blocks[0].Instructions[0]);
        Assert.Contains("= pure \"conv.f64.i32\"(%a)\n", IrText.Dump(p with { Blocks = [p.Blocks[0] with { Instructions = [flagged with { RuntimeSensitive = false }] }] }), StringComparison.Ordinal);
    }

    private static IrSortValue Element(int id) => new("System.Decimal", id);

    private static IrInputs Inputs() => new([Element(1), Element(2)]);

    private static IrRun Run(IrProcedure procedure, IPureOracle pure, Func<CallIdentity, bool>? taint = null) =>
        Run(procedure, Inputs().Arguments, pure, taint);

    private static IrRun Run(IrProcedure procedure, ImmutableArray<IrValue> inputs, IPureOracle pure, Func<CallIdentity, bool>? taint = null) =>
        IrInterpreter.Run(procedure, new IrInputs(inputs), new NoCalls(), 100, taint, pure);

    /// <summary>Answers every pure application with one value and fixed flags, and records what it was asked.</summary>
    private sealed class Answers(IrValue value, params bool[] flags) : IPureOracle
    {
        public List<(IrPure Pure, ImmutableArray<IrValue> Arguments)> Asked { get; } = [];

        public IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments)
        {
            Asked.Add((pure, arguments));
            return new IrPureResult(value, [.. flags.Take(pure.Throws.Length)]);
        }
    }

    /// <summary>Answers every call with the argument it was given, never throwing.</summary>
    private sealed class NoCalls : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap) =>
            new(resultType is null ? null : arguments[0], Threw: false);
    }
}
