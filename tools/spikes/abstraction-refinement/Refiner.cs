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
        Divergent => "Divergent",
        Unknown { Reason: UnknownReason.Timeout } => "timeout",
        Unknown unknown => $"still Unknown ({Name(unknown.Reason)})",
        _ => verdict.GetType().Name,
    };

    private static string Name(UnknownReason reason) => reason switch
    {
        UnknownReason.Abstraction => "abstraction",
        _ => reason.ToString(),
    };

    /// <summary>Queries both the pair as lowered (the baseline, all abstractions shared) and the pair refined.</summary>
    public static (Verdict Baseline, Verdict Refined) Query(IVerificationBackend backend, IrProcedure old, IrProcedure @new, VerificationOptions options) =>
        (backend.Verify(old, @new, options), backend.Verify(Interpret(old), Interpret(@new), options));
}
