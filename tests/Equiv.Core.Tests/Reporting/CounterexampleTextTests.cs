using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

using Xunit;

namespace Equiv.Core.Tests.Reporting;

/// <summary>One case per <see cref="IrOutcome"/> kind, since <see cref="CounterexampleText.Dump"/> switches on it.</summary>
public sealed class CounterexampleTextTests
{
    private static readonly IrInputs NoInputs = new([]);

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
