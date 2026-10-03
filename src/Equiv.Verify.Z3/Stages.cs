using System.Globalization;

using Equiv.Core;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// Where a pair's time goes, at <c>debug</c> (ticket P2-076 criterion 1). A <c>stage=… took=…s</c> line is one piece of
/// work, and no two overlap: <see cref="Share"/>, <see cref="Shape"/>, <see cref="Unroll"/>, <see cref="Encode"/>,
/// <see cref="Assert"/>, <see cref="Inline"/>, <see cref="Replay"/>, <see cref="Dispose"/>, <see cref="Couple"/>,
/// <see cref="EncodeChc"/>, <see cref="Propose"/> and each solver query, <c>stage=check:&lt;query&gt; took=…s result=sat</c>.
/// A <c>step=… took=…s</c> line is the whole of a step that is not a rung and holds the stages written since it began,
/// as a rung's line (ticket M4-014) holds its own. What a pair took beyond its stages is bookkeeping between them. A
/// line's text is built only when the log is at <c>debug</c>, and no line changes what is asked of the solver.
/// </summary>
internal static class Stages
{
    /// <summary>The fragments both sides share made calls (<see cref="ProductEncoder.ShareFragments"/>).</summary>
    public const string Share = "share";

    /// <summary>Both sides' loops found (<see cref="Equiv.Core.Ir.IrLoopAnalysis"/>).</summary>
    public const string Shape = "shape";

    /// <summary>The loops of both sides aligned and their header states paired (<see cref="LockstepInduction"/>).</summary>
    public const string Couple = "couple";

    /// <summary>One candidate invariant asked of a proposer (<see cref="Ladder.IInvariantProposer"/>).</summary>
    public const string Propose = "propose";

    /// <summary>Both sides unrolled to the bound (<see cref="Equiv.Core.Ir.IrUnroller"/>).</summary>
    public const string Unroll = "unroll";

    /// <summary>The product encoding (<see cref="ProductEncoder.Encode"/>).</summary>
    public const string Encode = "encode";

    /// <summary>The Horn clauses of rungs 4 and 5 (<see cref="ChcEncoder"/>).</summary>
    public const string EncodeChc = "encode-chc";

    /// <summary>A query's solver made and the encoding's assertions added to it.</summary>
    public const string Assert = "assert";

    /// <summary>A query with the definitions substituted in (<see cref="Z3Backend.Inline"/>), and added to its solver.</summary>
    public const string Inline = "inline";

    /// <summary>A model decoded and replayed through the interpreter (<see cref="ModelDecoder"/>).</summary>
    public const string Replay = "replay";

    /// <summary>A <see cref="Context"/> and every term in it released (<see cref="WithContext"/>).</summary>
    public const string Dispose = "dispose";

    /// <summary>Every opaque node some input reaches (<see cref="Z3Backend.ReachableOpaques"/>).</summary>
    public const string ReachableOpaques = "reachable-opaques";

    /// <summary>ADR 0037's two answers on an Unknown pair (<see cref="FailureRefinementQuery"/>).</summary>
    public const string FailureRefinement = "failure-refinement";

    /// <summary>The contracts admitted for a caller's callee pairs (<see cref="Contracts.ContractSearch"/>).</summary>
    public const string ContractSearch = "contract-search";

    /// <summary>The timestamp a stage or a step starts at.</summary>
    public static long Start() => TimeProvider.System.GetTimestamp();

    /// <summary>Runs <paramref name="work"/> as the stage <paramref name="stage"/>.</summary>
    public static T Timed<T>(VerificationOptions options, string stage, Func<T> work)
    {
        long started = Start();
        T result = work();
        Done(options, stage, started);
        return result;
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a context of its own, which is disposed on every path as the stage
    /// <see cref="Dispose"/>.
    /// </summary>
    public static T WithContext<T>(VerificationOptions options, Func<Context> createContext, Func<Context, T> work)
    {
        Context context = createContext();
        long started = Start();
        try
        {
            using (context)
            {
                try
                {
                    return work(context);
                }
                finally
                {
                    started = Start();
                }
            }
        }
        finally
        {
            Done(options, Dispose, started);
        }
    }

    /// <summary>The line of the stage <paramref name="stage"/>, which began at <paramref name="started"/>.</summary>
    public static void Done(VerificationOptions options, string stage, long started) => Write(options, "stage", stage, started, result: null);

    /// <summary>The line of the solver query <paramref name="query"/>, with the answer it gave.</summary>
    public static void Checked(VerificationOptions options, string query, long started, Status status) =>
        Write(options, "stage", "check:", started, status, query);

    /// <summary>The line of the step <paramref name="step"/>, which began at <paramref name="started"/>.</summary>
    public static void StepDone(VerificationOptions options, string step, long started) => Write(options, "step", step, started, result: null);

    private static void Write(VerificationOptions options, string kind, string name, long started, Status? result, string suffix = "")
    {
        if (!options.Log.IsDebug)
        {
            return;
        }

        double took = TimeProvider.System.GetElapsedTime(started).TotalSeconds;
        string answer = result switch
        {
            null => string.Empty,
            Status.SATISFIABLE => " result=sat",
            Status.UNSATISFIABLE => " result=unsat",
            _ => " result=unknown",
        };
        options.Log.Detail(string.Create(CultureInfo.InvariantCulture, $"{kind}={name}{suffix} took={took:0.###}s{answer}"));
    }
}
