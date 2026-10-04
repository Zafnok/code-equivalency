using System.Globalization;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3;

using Microsoft.Z3;

namespace FailureRefinementSpike;

/// <summary>
/// One of ADR 0037's two queries on a pair: the outcome criterion 2 names (<c>none-proved</c>, <c>found</c>,
/// <c>unknown</c>, or <c>timeout</c> when a solver check of it gave up) and the time its solver checks took.
/// </summary>
internal sealed record QueryMeasure(string Outcome, double SolverSeconds);

/// <summary>Both queries on a pair, and the time the production step took in all (unrolling, encoding and checks).</summary>
internal sealed record PairMeasure(QueryMeasure NewFailures, QueryMeasure RemovedFailures, double ElapsedSeconds);

/// <summary>
/// Runs the production <see cref="FailureRefinementQuery"/> on a pair, with nothing of it copied: the per-query solver
/// time and whether a check gave up are read from the <c>check:</c> lines it writes to the run log at debug (ADR 0038).
/// </summary>
internal static partial class Measured
{
    public const string Timeout = "timeout";

    public static PairMeasure Run(IrProcedure old, IrProcedure @new, VerificationOptions options, bool encodable)
    {
        CheckLog log = new();
        FailureRefinement refinement = new FailureRefinementQuery(static () => new Context(), options with { Log = log }).Run(old, @new, encodable);

        // Each query checks failure-modelled and, only when that is unsatisfiable, failure-resolved; newFailures runs first.
        Queue<(string Result, double Seconds)> checks = new(log.Checks);
        return new PairMeasure(Take(refinement.NewFailures, checks), Take(refinement.RemovedFailures, checks), refinement.Elapsed.TotalSeconds);
    }

    private static QueryMeasure Take(RefinementResult result, Queue<(string Result, double Seconds)> checks)
    {
        List<(string Result, double Seconds)> own = [];
        if (checks.TryDequeue(out (string Result, double Seconds) modelled))
        {
            own.Add(modelled);
            if (modelled.Result == "unsat")
            {
                own.Add(checks.Dequeue());
            }
        }

        string outcome = result.Outcome switch
        {
            RefinementOutcome.NoneProved => "none-proved",
            RefinementOutcome.Found => "found",
            _ => own.Any(static c => c.Result == "unknown") ? Timeout : "unknown",
        };
        return new QueryMeasure(outcome, own.Sum(static c => c.Seconds));
    }

    [GeneratedRegex(@"^stage=check:failure-(?:modelled|resolved) took=([0-9.]+)s result=(\w+)$")]
    private static partial Regex CheckLine();

    /// <summary>Keeps the failure-refinement <c>check:</c> lines of one pair, in order.</summary>
    private sealed class CheckLog : IRunLog
    {
        public List<(string Result, double Seconds)> Checks { get; } = [];

        public bool IsDebug => true;

        public void Detail(string text)
        {
            if (CheckLine().Match(text) is { Success: true } match)
            {
                Checks.Add((match.Groups[2].Value, double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)));
            }
        }

        public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null)
        {
        }

        public void Item(string identity, long weight)
        {
        }

        public void ItemDone(string outcome)
        {
        }

        public void PhaseDone()
        {
        }
    }
}
