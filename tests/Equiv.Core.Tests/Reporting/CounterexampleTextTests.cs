using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>One case per <see cref="IrOutcome"/> kind, since <see cref="CounterexampleText.Dump"/> switches on it.</summary>
public sealed class CounterexampleTextTests
{
    private static readonly IrInputs NoInputs = new([]);

    /// <summary>Public since ticket P1-001, whose rung 4 renders a spurious derivation's replay with it.</summary>
    [Fact]
    public void DumpRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(static () => CounterexampleText.Dump(Null.Of<Counterexample>()!));
    }

    [Fact]
    public void ReturnedWithNoValueDumpsAsReturned()
    {
        IrRun run = new(new IrReturned(Value: null), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("returned outs() trace()", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnedWithAValueDumpsTheValue()
    {
        IrRun run = new(new IrReturned(new IrBitVecValue(32, 7)), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("returned bv32 7", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0053 decision 5 (ticket P1-030): a <c>float</c> or <c>double</c> value is written as its number wherever a
    /// value is written: an input, a returned value, an out, a call argument, a heap slice, and inside a map.
    /// </summary>
    [Fact]
    public void AFloatingPointValueDumpsAsItsNumberEverywhere()
    {
        IrMap cells = new(IrFloat.Binary32, IrFloat.Binary64);
        IrMapValue map = new IrMapValue(cells, IrFloat.Of(0.0), []).Write(IrFloat.Of(2f), IrFloat.Of(-0.0)).Write(IrFloat.Of(1f), IrFloat.Of(double.NaN));
        IrCallRecord call = new(new CallIdentity("Log::Write(double)"), [IrFloat.Of(0.1)]) { Heap = [new IrHeapSlice("field.C.x", map)] };
        IrRun run = new(new IrReturned(IrFloat.Of(1e300)), [IrFloat.Of(float.PositiveInfinity), new IrSortValue("System.Decimal", 3)], [call]);
        Counterexample counterexample = new(new IrInputs([IrFloat.Of(1.5), IrFloat.Of(-2.5f), new IrBitVecValue(32, 7), map]), run, run);

        string text = CounterexampleText.Dump(counterexample);

        const string Map = "map<sort \"System.Single\", sort \"System.Double\"> [f32 1 -> f64 NaN, f32 2 -> f64 -0] default f64 0";
        Assert.StartsWith($"inputs(f64 1.5, f32 -2.5, bv32 7, {Map}) old(returned f64 1E+300 outs(f32 Infinity, sort \"System.Decimal\" 3) ", text, StringComparison.Ordinal);
        Assert.Contains($"trace(\"Log::Write(double)\"(f64 0.1) heap(\"field.C.x\" {Map}))", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sort \"System.Double\" ", text.Replace("sort \"System.Double\">", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void ThrewDumpsTheExceptionType()
    {
        IrRun run = new(new IrThrew("System.OverflowException"), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("threw \"System.OverflowException\"", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void InfeasibleDumpsAsInfeasible()
    {
        IrRun run = new(new IrInfeasible(), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("infeasible", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void BudgetExhaustedDumpsAsBudgetExhausted()
    {
        IrRun run = new(new IrBudgetExhausted(), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("budget-exhausted", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void OpaqueReachedDumpsTheReason()
    {
        IrRun run = new(new IrOpaqueReached("lock statement", new SourceSpan("Samples/Orders.cs", 12, 9, 12, 20)), [], []);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("opaque-reached \"lock statement\"", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void TraceIncludesCallsAndArguments()
    {
        IrRun run = new(new IrInfeasible(), [], [new IrCallRecord(new CallIdentity("Samples.Svc::Next(int32)"), [new IrBitVecValue(32, 1)])]);
        Counterexample counterexample = new(NoInputs, run, run);
        Assert.Contains("\"Samples.Svc::Next(int32)\"(bv32 1)", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }

    [Fact]
    public void TraceIncludesTheHeapACallReadOnlyWhenItReadOne()
    {
        IrMapValue heap = new(new IrMap(new IrBitVec(32), new IrBitVec(32)), new IrBitVecValue(32, 0), []);
        IrRun run = new(
            new IrInfeasible(),
            [],
            [new IrCallRecord(new CallIdentity("F"), []) { Heap = [new IrHeapSlice("field.C.x", heap)] }, new IrCallRecord(new CallIdentity("G"), [])]);
        Counterexample counterexample = new(NoInputs, run, run);

        Assert.Contains("trace(\"F\"() heap(\"field.C.x\" map<bv32, bv32> [] default bv32 0), \"G\"())", CounterexampleText.Dump(counterexample), StringComparison.Ordinal);
    }
}
