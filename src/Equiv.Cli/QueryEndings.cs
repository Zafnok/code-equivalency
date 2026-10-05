using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>
/// How many solver queries a limit ended, by the limit: the deterministic resource limit, or the wall-clock backstop
/// behind it (ticket P2-077 criterion 4), as <c>run.properties.queryEndings</c>. A query a limit ends times its rung
/// out, and the rung's detail names the limit (ticket P2-050 criterion 4), so the counts are of the timed-out rungs in
/// the ladders of the run's results. A run in which threads share processors must not have more wall-clock endings
/// than the same run on one thread.
/// </summary>
internal sealed record QueryEndings(int ResourceLimit, int WallClock)
{
    /// <summary>How the detail of a rung that the resource limit ended names it (<c>Z3Backend.LimitHit</c>).</summary>
    internal const string ResourceLimitHit = ": resource limit ";

    /// <summary>How the detail of a rung that the wall-clock backstop ended names it (<c>Z3Backend.LimitHit</c>).</summary>
    internal const string WallClockHit = ": wall-clock limit ";

    public static QueryEndings Of(IEnumerable<VerificationResult> results)
    {
        string[] details = [.. results.SelectMany(static r => r.Verdict.Ladder).Where(static step => step.Outcome == RungOutcome.Timeout).Select(static step => step.Detail)];
        return new QueryEndings(
            details.Count(static d => d.Contains(ResourceLimitHit, StringComparison.Ordinal)),
            details.Count(static d => d.Contains(WallClockHit, StringComparison.Ordinal)));
    }

    public Dictionary<string, object> ToProperty() => new(StringComparer.Ordinal)
    {
        ["resourceLimit"] = ResourceLimit,
        ["wallClock"] = WallClock,
    };
}
