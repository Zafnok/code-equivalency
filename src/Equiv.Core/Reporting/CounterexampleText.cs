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
/// a replay in an Unknown's detail (rung 4's spurious derivation, ticket P1-001). A <c>float</c> or <c>double</c> value
/// is written as its number (<see cref="IrFloat.Text"/>; ADR 0053 decision 5), not as the bits its element's id is.
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
        IrReturned returned => returned.Value is null ? "returned" : "returned " + Value(returned.Value),
        IrThrew threw => "threw " + IrText.Quote(threw.ExceptionType),
        IrInfeasible => "infeasible",
        IrBudgetExhausted => "budget-exhausted",
        _ => "opaque-reached " + IrText.Quote(((IrOpaqueReached)outcome).Reason),
    };

    private static string Values(ImmutableArray<IrValue> values) => string.Join(", ", values.Select(Value));

    /// <summary><see cref="IrText.Value"/>, with a floating-point number as its number, inside a map too.</summary>
    private static string Value(IrValue value) => value switch
    {
        IrSortValue element => IrFloat.Text(element) ?? IrText.Value(element),
        IrMapValue map => $"{IrText.Type(map.MapType)} [{string.Join(", ", map.Entries.Select(static e => $"{Value(e.Key)} -> {Value(e.Value)}").Order(StringComparer.Ordinal))}] default {Value(map.Default)}",
        _ => IrText.Value(value),
    };

    /// <summary>Each call with its arguments, and the heap it read when it read any (ticket P1-005), so that a heap difference at a call shows.</summary>
    private static string Trace(ImmutableArray<IrCallRecord> trace) =>
        string.Join(", ", trace.Select(static c => $"{IrText.Quote(c.Callee.Value)}({Values(c.Arguments)}){Heap(c.Heap)}"));

    private static string Heap(ImmutableArray<IrHeapSlice> heap) =>
        heap.IsEmpty ? string.Empty : $" heap({string.Join(", ", heap.Select(static h => $"{IrText.Quote(h.Map)} {Value(h.Value)}"))})";
}
