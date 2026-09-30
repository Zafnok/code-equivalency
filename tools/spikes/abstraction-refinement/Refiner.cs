using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace AbstractionSpike;

/// <summary>
/// ARDiff-style refinement, measured and not built: every <see cref="IrPure"/> of a closed-form kind becomes its real
/// meaning (one <see cref="IrBinary"/>, and each exception flag a constant <c>false</c>), and the pair is queried again with
/// the backend and options a run uses. Every other abstraction stays shared.
/// </summary>
internal static class Refiner
{
    public static IrProcedure Interpret(IrProcedure body) =>
        body with { Blocks = [.. body.Blocks.Select(static b => b with { Instructions = [.. b.Instructions.SelectMany(Rewrite)] })] };

    public static int Count(IrProcedure body) =>
        body.Blocks.Sum(static b => b.Instructions.Count(static i => i is IrPure pure && Kinds.Interpretations.ContainsKey(pure.Function)));

    private static IEnumerable<IrInstruction> Rewrite(IrInstruction instruction)
    {
        if (instruction is not IrPure pure || !Kinds.Interpretations.TryGetValue(pure.Function, out IrBinaryOp op))
        {
            yield return instruction;
            yield break;
        }

        yield return new IrBinary(pure.Target, op, pure.Args[0], pure.Args[1]);
        foreach (IrPureThrow thrown in pure.Throws)
        {
            yield return new IrConst(thrown.Flag, new IrBoolValue(false));
        }
    }

    /// <summary>The outcome names criterion 2 records.</summary>
    public static string Outcome(Verdict verdict) => verdict switch
    {
        Equivalent => "Equivalent",
        Divergent divergent => $"Divergent ({Rule(divergent.Counterexample)}; {Where(divergent.Counterexample)})",
        Unknown { Reason: UnknownReason.Timeout } => "timeout",
        Unknown unknown => $"still Unknown ({Name(unknown.Reason)})",
        _ => verdict.GetType().Name,
    };

    /// <summary>EQ006 when a runtime-changed callee is in either trace, as the SARIF writer decides (VerdictRule); else EQ002.</summary>
    private static string Rule(Counterexample counterexample) =>
        counterexample.Old.Trace.Concat(counterexample.New.Trace).Any(static r => r.Callee.RuntimeChanged) ? "EQ006" : "EQ002";

    /// <summary>
    /// Which observable differs, by kind and callee identity only, never a value: the first call whose callee differs,
    /// a call on one side only, the same call with other arguments or heap, or else the outcome or the outs.
    /// </summary>
    private static string Where(Counterexample counterexample)
    {
        ImmutableArray<IrCallRecord> old = counterexample.Old.Trace;
        ImmutableArray<IrCallRecord> @new = counterexample.New.Trace;
        for (int i = 0; i < Math.Min(old.Length, @new.Length); i++)
        {
            if (old[i].Callee != @new[i].Callee)
            {
                return $"call {i}: `{Callee(old[i])}` against `{Callee(@new[i])}`";
            }

            if (!old[i].Equals(@new[i]))
            {
                return $"call {i} `{Callee(old[i])}`: arguments or heap differ";
            }
        }

        if (old.Length != @new.Length)
        {
            IrCallRecord extra = old.Length > @new.Length ? old[@new.Length] : @new[old.Length];
            return $"call {Math.Min(old.Length, @new.Length)} on the {(old.Length > @new.Length ? "legacy" : "modern")} side only: `{Callee(extra)}`";
        }

        return counterexample.Old.Outcome.GetType() != counterexample.New.Outcome.GetType()
            ? $"outcome: {counterexample.Old.Outcome.GetType().Name} against {counterexample.New.Outcome.GetType().Name}"
            : $"{counterexample.Old.Outcome.GetType().Name} value or outs";
    }

    private static string Callee(IrCallRecord record) => record.Callee.Value + (record.Callee.RuntimeChanged ? "!" : string.Empty);

    private static string Name(UnknownReason reason) => reason switch
    {
        UnknownReason.Abstraction => "abstraction",
        _ => reason.ToString(),
    };

    /// <summary>Queries both the pair as lowered (the baseline, all abstractions shared) and the pair refined.</summary>
    public static (Verdict Baseline, Verdict Refined) Query(IVerificationBackend backend, IrProcedure old, IrProcedure @new, VerificationOptions options) =>
        (backend.Verify(old, @new, options), backend.Verify(Interpret(old), Interpret(@new), options));
}
