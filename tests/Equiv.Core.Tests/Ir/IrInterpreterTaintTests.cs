using System.Collections.Immutable;

using CsCheck;

using Equiv.Core.Ir;
using Equiv.TestSupport;

using Xunit;

namespace Equiv.Core.Tests.Ir;

/// <summary>
/// The taint shadow of <see cref="IrInterpreter"/> (ADR 0026; ticket M3-016 criteria 1, 2 and 6): which observables
/// depend on a call the predicate marks, through data and through control, and that taint never changes a value.
/// </summary>
public sealed class IrInterpreterTaintTests
{
    private const string Header = "proc \"T::M\" ";

    private static readonly ICallOracle Answers = new Oracle();

    private static readonly CallIdentity Opaque = new("opaque:f");

    [Fact]
    public void TaintFlowsThroughDataDependencies()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32, %c: bool, ref %r: bv32, ref %s: bv32, ref %m: map<bv32, bv32>) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:f"(%a) threw %th: bool
              %u: bv32 = add %t, %a
              %w: bool = eq %u, %a
              %n: bool = boolnot %w
              %v: bv32 = add %a, %a
              %m1: map<bv32, bv32> = mapwrite %m, %a, %u
              %k: bv32 = mapread %m1, %a
              call "log"(%u)
              call "log"(%v)
              br %c, B1, B2
            B1:
              goto B2
            B2:
              %p: bv32 = phi [B0: %v, B1: %u]
              ret %k outs(%r = %p, %s = %v, %m = %m1)
            """);

        IrRun viaB0 = Run(p, Bv(1), new IrBoolValue(Value: false), Bv(0), Bv(0), Map());
        IrRun viaB1 = Run(p, Bv(1), new IrBoolValue(Value: true), Bv(0), Bv(0), Map());

        Assert.Equal(new IrTaint(Outcome: false, Value: true, [2], [0, 1], [Opaque]), viaB0.Taint);
        Assert.Equal(new IrTaint(Outcome: false, Value: true, [0, 2], [0, 1], [Opaque]), viaB1.Taint);
        Assert.True(viaB1.OutTainted(0));
        Assert.False(viaB1.OutTainted(1));
        Assert.True(viaB1.EventTainted(1));
        Assert.False(viaB1.EventTainted(2));
        Assert.False(viaB1.EventTainted(3));
    }

    [Fact]
    public void ATaintedCallsThrewFlagAndEveryCallFedByTaintAreTainted()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bool entry B0
            B0:
              %t: bv32 = call "opaque:f"(%a) threw %th: bool
              %x: bv32 = call "g"(%t)
              %y: bv32 = call "g"(%a)
              ret %th
            """);

        IrRun run = Run(p, Bv(1));

        Assert.Equal(new IrTaint(Outcome: false, Value: true, [], [0, 1], [Opaque]), run.Taint);
    }

    [Fact]
    public void AnUntaintedReturnValueAndAVoidReturnAreUntainted()
    {
        IrProcedure value = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:f"(%a)
              ret %a
            """);
        IrProcedure none = IrText.Parse(Header + """
            (%a: bv32) entry B0
            B0:
              %t: bv32 = call "opaque:f"(%a)
              ret
            """);

        Assert.Equal(new IrTaint(Outcome: false, Value: false, [], [0], [Opaque]), Run(value, Bv(1)).Taint);
        Assert.Equal(new IrTaint(Outcome: false, Value: false, [], [0], [Opaque]), Run(none, Bv(1)).Taint);
    }

    [Fact]
    public void BranchOnTaintTaintsTheRestOfTheSide()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32, ref %r: bv32) -> bv32 entry B0
            B0:
              %v: bv32 = add %a, %a
              call "before"(%a)
              %t: bool = call "opaque:f"(%a)
              br %t, B1, B2
            B1:
              %k: bv32 = const bv32 7
              call "after"(%a)
              ret %a outs(%r = %k)
            B2:
              throw "E" outs(%r = %v)
            """);

        IrRun then = Run(p, Bv(1), Bv(0));
        IrRun @else = Run(p, Bv(2), Bv(0));

        Assert.IsType<IrReturned>(then.Outcome);
        Assert.IsType<IrThrew>(@else.Outcome);
        Assert.Equal(new IrTaint(Outcome: true, Value: true, [0], [1, 2], [Opaque]), then.Taint);
        Assert.Equal(new IrTaint(Outcome: true, Value: true, [0], [1], [Opaque]), @else.Taint);
        Assert.False(@else.EventTainted(0));
        Assert.True(@else.EventTainted(2));
    }

    [Fact]
    public void ASwitchOnTaintTaintsTheRestOfTheSideAndAnUntaintedOneDoesNot()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              switch %a [bv32 1 -> B1] default B1
            B1:
              %t: bv32 = call "opaque:f"(%a)
              switch %t [bv32 1 -> B2] default B2
            B2:
              %k: bv32 = const bv32 7
              ret %k
            """);

        Assert.Equal(new IrTaint(Outcome: true, Value: true, [], [0], [Opaque]), Run(p, Bv(1)).Taint);
    }

    [Fact]
    public void ABranchOnAnUntaintedConditionLeavesThePathUntainted()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%c: bool) -> bv32 entry B0
            B0:
              %t: bv32 = call "opaque:f"(%c)
              br %c, B1, B1
            B1:
              %k: bv32 = const bv32 7
              ret %k
            """);

        Assert.Equal(new IrTaint(Outcome: false, Value: false, [], [0], [Opaque]), Run(p, new IrBoolValue(Value: true)).Taint);
    }

    [Fact]
    public void AValueRedefinedAfterTaintIsUntaintedAgain()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              %zero: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %t: bv32 = call "opaque:f"(%a)
              goto B1
            B1:
              %i: bv32 = phi [B0: %zero, B2: %next]
              %z: bv32 = phi [B0: %t, B2: %a]
              %more: bool = ult %i, %one
              br %more, B2, B3
            B2:
              %next: bv32 = add %i, %one
              goto B1
            B3:
              ret %z
            """);

        IrRun run = Run(p, Bv(5));

        Assert.Equal(new IrReturned(Bv(5)), run.Outcome);
        Assert.False(run.Taint.Value);
    }

    [Fact]
    public void ARunThatStopsEarlyCarriesThePathTaint()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) entry B0
            B0:
              %t: bool = call "opaque:f"(%a)
              br %t, B1, B1
            B1:
              opaque "lambda" at "T.cs" 3:9-3:20
              ret
            """);

        IrRun run = Run(p, Bv(1));

        Assert.IsType<IrOpaqueReached>(run.Outcome);
        Assert.Equal(new IrTaint(Outcome: true, Value: true, [], [0], [Opaque]), run.Taint);
    }

    [Fact]
    public void EachTaintingIdentityIsASourceOnceInFirstReachedOrder()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) entry B0
            B0:
              %x: bv32 = call "opaque:g"(%a)
              %y: bv32 = call "opaque:f"(%a)
              %z: bv32 = call "opaque:g"(%a)
              ret
            """);

        Assert.Equal([new CallIdentity("opaque:g"), Opaque], Run(p, Bv(1)).Taint.Sources);
    }

    [Fact]
    public void WithoutAPredicateNothingIsTainted()
    {
        IrProcedure p = IrText.Parse(Header + """
            (%a: bv32) -> bv32 entry B0
            B0:
              %t: bool = call "opaque:f"(%a)
              br %t, B1, B1
            B1:
              ret %a
            """);

        IrRun run = IrInterpreter.Run(p, new IrInputs([Bv(1)]), Answers, 100);

        Assert.Equal(IrTaint.None, run.Taint);
        Assert.False(run.EventTainted(0));
        Assert.False(run.EventTainted(1));
    }

    [Fact]
    public void TaintIsPartOfARunsEquality()
    {
        IrRun plain = new(new IrReturned(Value: null), [], []);
        IrRun tainted = plain with { Taint = new IrTaint(Outcome: true, Value: true, [], [], [Opaque]) };

        Assert.NotEqual(plain, tainted);
        Assert.Equal(tainted, plain with { Taint = new IrTaint(Outcome: true, Value: true, [], [], [Opaque]) });
        Assert.Equal(tainted.GetHashCode(), (plain with { Taint = new IrTaint(Outcome: true, Value: true, [], [], [Opaque]) }).GetHashCode());
    }

    [Theory]
    [InlineData(false, true, 0, 0, false)]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(true, true, 1, 0, false)]
    [InlineData(true, true, 0, 1, false)]
    [InlineData(true, true, 0, 0, true)]
    public void TaintsDifferingInOneFieldAreUnequal(bool outcome, bool value, int @out, int @event, bool otherSource)
    {
        IrTaint baseline = new(Outcome: true, Value: true, [0], [0], [Opaque]);
        IrTaint other = new(outcome, value, [@out], [@event], [otherSource ? new CallIdentity("opaque:g") : Opaque]);

        Assert.NotEqual(baseline, other);
        Assert.False(baseline.Equals(Null.Of<IrTaint>()));
    }

    /// <summary>
    /// Criterion 6's property: without a taint predicate a run is exactly what it was before taint existed (every
    /// other interpreter test pins those values), and with any predicate the values stay the same; only
    /// <see cref="IrRun.Taint"/> changes.
    /// </summary>
    [Fact]
    public void TaintNeverChangesAValue()
    {
        IrGen.Procedure
            .SelectMany(static p => Gen.Select(IrGen.Inputs(p), Gen.Bool, (i, all) => (p, i, all)))
            .Sample(
                static s =>
                {
                    (IrProcedure procedure, IrInputs input, bool all) = s;
                    IrRun plain = IrGen.Run(procedure, input);
                    IrRun tainted = IrInterpreter.Run(procedure, input, IrGenOracle.Instance, IrGen.StepBudget, all ? static _ => true : static c => c.Value.Length % 2 == 0, IrGenOracle.Instance);
                    Assert.Equal(IrTaint.None, plain.Taint);
                    Assert.Equal(plain, tainted with { Taint = IrTaint.None });
                    Assert.Equal(plain, IrInterpreter.Run(procedure, input, IrGenOracle.Instance, IrGen.StepBudget, static _ => false, IrGenOracle.Instance) with { Taint = IrTaint.None });
                },
                iter: 200,
                print: static s => IrText.Dump(s.p));
    }

    private static IrBitVecValue Bv(ulong bits) => new(32, bits);

    private static IrMapValue Map() => new(new IrMap(new IrBitVec(32), new IrBitVec(32)), Bv(0), []);

    private static IrRun Run(IrProcedure procedure, params IrValue[] args) =>
        IrInterpreter.Run(procedure, new IrInputs([.. args]), Answers, 1000, static c => c.Value.StartsWith("opaque:", StringComparison.Ordinal));

    /// <summary>Answers every call with its first argument's bits (or 1 without arguments), and <c>threw</c> false.</summary>
    private sealed class Oracle : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position) => resultType switch
        {
            null => new IrCallResult(Value: null, Threw: false),
            IrBool => new IrCallResult(new IrBoolValue(arguments is [IrBitVecValue { Bits: 1 }, ..]), Threw: false),
            _ => new IrCallResult(arguments is [IrBitVecValue first, ..] ? first : new IrBitVecValue(32, 1), Threw: false),
        };
    }
}
