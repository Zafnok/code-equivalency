using System.Collections.Immutable;
using System.Text;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Core.Reporting;

/// <summary>
/// A deterministic text rendering of a <see cref="Counterexample"/>, reusing
/// <see cref="IrText"/>'s value/quoting rules so the same literal always renders the same way.
/// Used for the SARIF result message and for <c>properties.model</c> (VERIFICATION-MODEL.md
/// section 6, EQ002) and, via <see cref="ResultFingerprint"/>, for the baseline fingerprint, and by a backend that states
/// a replay in an Unknown's detail (rung 4's spurious derivation, ticket P1-001).
/// </summary>
public static class CounterexampleText
{
    public static string Dump(Counterexample counterexample)
    {
        ArgumentNullException.ThrowIfNull(counterexample);
        StringBuilder text = new();
        text.Append("inputs(").Append(Values(counterexample.Inputs.Arguments)).Append(") ")
            .Append("old(").Append(Run(counterexample.Old)).Append(") ")
            .Append("new(").Append(Run(counterexample.New)).Append(')');
        return text.ToString();
    }

    private static string Run(IrRun run) =>
        $"{Outcome(run.Outcome)} outs({Values(run.Outs)}) trace({Trace(run.Trace)})";

    /// <summary>
    /// <see cref="IrOutcome"/> is a closed hierarchy (private protected constructor) with five
    /// members; the final arm covers <see cref="IrOpaqueReached"/>, mirroring the same
    /// last-arm-assumed pattern as <see cref="VerdictRule.Describe"/>.
    /// </summary>
    private static string Outcome(IrOutcome outcome) => outcome switch
    {
        IrReturned returned => returned.Value is null ? "returned" : "returned " + IrText.Value(returned.Value),
        IrThrew threw => "threw " + IrText.Quote(threw.ExceptionType),
        IrInfeasible => "infeasible",
        IrBudgetExhausted => "budget-exhausted",
        _ => "opaque-reached " + IrText.Quote(((IrOpaqueReached)outcome).Reason),
    };

    private static string Values(ImmutableArray<IrValue> values) => string.Join(", ", values.Select(IrText.Value));

    /// <summary>Each call with its arguments, and the heap it read when it read any (ticket P1-005), so that a heap difference at a call shows.</summary>
    private static string Trace(ImmutableArray<IrCallRecord> trace) =>
        string.Join(", ", trace.Select(static c => $"{IrText.Quote(c.Callee.Value)}({Values(c.Arguments)}){Heap(c.Heap)}"));

    private static string Heap(ImmutableArray<IrHeapSlice> heap) =>
        heap.IsEmpty ? string.Empty : $" heap({string.Join(", ", heap.Select(static h => $"{IrText.Quote(h.Map)} {IrText.Value(h.Value)}"))})";
}
