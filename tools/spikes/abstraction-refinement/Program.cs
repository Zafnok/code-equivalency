using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

namespace AbstractionSpike;

/// <summary>
/// P1-019: of a full run's <c>abstraction</c> Unknowns, how many would giving the tainting abstraction its real meaning
/// resolve, and which way? Reads the run's SARIF, groups each result's <c>properties.abstractions</c> by kind, and, given
/// both solutions, lowers them again, names each <c>opaque:</c> fragment's reason, and re-queries every pair holding a
/// closed-form kind with that kind interpreted. Prints identities, kinds and counts; never a candidate value or source text.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args.Length is not (1 or 3))
        {
            Console.Error.WriteLine("usage: abstraction-refinement-spike --self-test | <equiv.sarif> [<legacy.sln> <modern.sln>]");
            return 2;
        }

        (int unknowns, List<AbstractionResult> results) = ReadSarif(args[0]);
        Dictionary<string, ProcedurePair>? pairs = args.Length == 3 ? Load(args[1], args[2]) : null;
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        Z3Backend backend = new();
        List<Row> rows = [];
        foreach (AbstractionResult result in results)
        {
            ProcedurePair? pair = pairs?.GetValueOrDefault(result.Identity);
            ImmutableArray<(string Kind, KindClass Class)> kinds = [.. result.Entries.Select(e => (Kind(e, pair), Kinds.Classify(e)))];
            string outcome = "not re-queried";
            string baseline = "n/a";
            if (kinds.Any(static k => k.Class == KindClass.ClosedForm))
            {
                if (pair is not { OldBody: { } old, NewBody: { } @new })
                {
                    outcome = pairs is null ? "not re-queried (no solutions given)" : "pair not found at this commit";
                }
                else
                {
                    Stopwatch clock = Stopwatch.StartNew();
                    (Verdict shared, Verdict refined) = Refiner.Query(backend, old, @new, options);
                    baseline = Refiner.Outcome(shared);
                    outcome = Refiner.Outcome(refined);
                    Console.Error.WriteLine($"re-queried {result.Identity} in {clock.ElapsedMilliseconds} ms");
                }
            }

            rows.Add(new Row(result.Identity, result.Scope, kinds, baseline, outcome));
        }

        Report.Print(unknowns, rows);
        return 0;
    }

    /// <summary>An <c>opaque:</c> entry's kind is its fragment's reason, found in either lowered body of the pair.</summary>
    private static string Kind(string identity, ProcedurePair? pair)
    {
        if (!identity.StartsWith(Kinds.OpaquePrefix, StringComparison.Ordinal))
        {
            return identity;
        }

        if (pair is null)
        {
            return "opaque";
        }

        string fingerprint = identity[Kinds.OpaquePrefix.Length..];
        string? reason = new[] { pair.OldBody, pair.NewBody }
            .OfType<IrProcedure>()
            .SelectMany(static b => b.Blocks)
            .SelectMany(static b => b.Instructions)
            .OfType<IrOpaque>()
            .FirstOrDefault(o => string.Equals(o.Fingerprint, fingerprint, StringComparison.Ordinal))?.Reason;
        return "opaque:" + (reason ?? "(fingerprint not found at this commit)");
    }

    private static Dictionary<string, ProcedurePair> Load(string legacy, string modern)
    {
        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        return pairs;
    }

    private static (int Unknowns, List<AbstractionResult> Results) ReadSarif(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        int unknowns = 0;
        List<AbstractionResult> results = [];
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (!string.Equals(result.GetProperty("ruleId").GetString(), "EQ003", StringComparison.Ordinal))
            {
                continue;
            }

            unknowns++;
            if (!result.TryGetProperty("properties", out JsonElement properties)
                || !properties.TryGetProperty("unknownReason", out JsonElement reason)
                || !string.Equals(reason.GetString(), "abstraction", StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new AbstractionResult(
                result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!,
                properties.TryGetProperty("scope", out JsonElement scope) ? scope.GetString()! : "n/a",
                [.. properties.GetProperty("abstractions").EnumerateArray().Select(static a => a.GetProperty("identity").GetString()!)]));
        }

        return (unknowns, results);
    }
}

internal sealed record AbstractionResult(string Identity, string Scope, ImmutableArray<string> Entries);

/// <summary>One <c>abstraction</c> Unknown: its kinds, the shared query's outcome at this commit, and the refined one.</summary>
internal sealed record Row(string Identity, string Scope, ImmutableArray<(string Kind, KindClass Class)> Kinds, string Baseline, string Outcome);

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-abstraction-spike.md</c> quotes.</summary>
internal static class Report
{
    private static readonly string[] Outcomes = ["Equivalent", "Divergent", "still Unknown", "timeout"];

    public static void Print(int unknowns, List<Row> rows)
    {
        int total = rows.Count;
        Console.WriteLine(Invariant($"Unknown results (EQ003) {unknowns}; abstraction {total} ({Share(total, unknowns)} of Unknowns); scope: {string.Join(", ", rows.GroupBy(static r => r.Scope).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key} {g.Count()}"))}"));

        Console.WriteLine();
        Console.WriteLine("## By class (results holding at least one entry of the class; a result can hold several)");
        Console.WriteLine("| class | results | share of abstraction Unknowns | results holding only this class |");
        Console.WriteLine("|---|---|---|---|");
        foreach (KindClass kindClass in Enum.GetValues<KindClass>())
        {
            int holding = rows.Count(r => r.Kinds.Any(k => k.Class == kindClass));
            int only = rows.Count(r => r.Kinds.All(k => k.Class == kindClass));
            Console.WriteLine(Invariant($"| {kindClass} | {holding} | {Share(holding, total)} | {only} |"));
        }

        Console.WriteLine();
        Console.WriteLine("## Kind by outcome (results holding the kind; the re-query interprets every closed-form kind of the pair at once)");
        Console.WriteLine($"| kind | class | results | share of abstraction Unknowns | entries | {string.Join(" | ", Outcomes)} | not re-queried |");
        Console.WriteLine($"|---|---|---|---|---|{string.Concat(Enumerable.Repeat("---|", Outcomes.Length))}---|");
        var kinds = rows
            .SelectMany(static r => r.Kinds.Select(static k => k).Distinct().Select(k => (k.Kind, k.Class, Row: r)))
            .GroupBy(static k => (k.Kind, k.Class))
            .Select(g => (g.Key.Kind, g.Key.Class, Rows: g.Select(static k => k.Row).ToList(), Entries: rows.Sum(r => r.Kinds.Count(k => string.Equals(k.Kind, g.Key.Kind, StringComparison.Ordinal)))))
            .OrderByDescending(static k => k.Rows.Count).ThenByDescending(static k => k.Entries).ThenBy(static k => k.Kind, StringComparer.Ordinal)
            .ToList();
        foreach (var kind in kinds)
        {
            IEnumerable<string> byOutcome = Outcomes.Select(o => Cell(kind.Rows.Count(r => r.Outcome.StartsWith(o, StringComparison.Ordinal)), total));
            int notQueried = kind.Rows.Count(static r => !Outcomes.Any(o => r.Outcome.StartsWith(o, StringComparison.Ordinal)));
            Console.WriteLine(Invariant($"| `{kind.Kind}` | {kind.Class} | {kind.Rows.Count} | {Share(kind.Rows.Count, total)} | {kind.Entries} | {string.Join(" | ", byOutcome)} | {Cell(notQueried, total)} |"));
        }

        Console.WriteLine();
        Console.WriteLine("## Ten most frequent kinds (by results holding them)");
        foreach (var kind in kinds.Take(10))
        {
            Console.WriteLine(Invariant($"| `{kind.Kind}` | {kind.Rows.Count} | {kind.Entries} |"));
        }

        Console.WriteLine();
        Console.WriteLine("## Re-queried results (procedure identity, kinds, outcome with every abstraction shared at this commit, outcome refined)");
        List<Row> queried = [.. rows.Where(static r => r.Kinds.Any(static k => k.Class == KindClass.ClosedForm))];
        foreach (Row row in queried.OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{row.Identity}` | {string.Join(", ", row.Kinds.Select(static k => k.Kind).Distinct().Select(static k => $"`{k}`"))} | {row.Baseline} | {row.Outcome} |");
        }

        int resolved = queried.Count(static r => r.Outcome is "Equivalent" or "Divergent");
        Console.WriteLine();
        Console.WriteLine(Invariant($"re-queried {queried.Count}; resolved (Equivalent or Divergent) {resolved}: {Share(resolved, total)} of abstraction Unknowns, {Share(resolved, unknowns)} of all Unknowns (ADR 0028's bar: 5% of all Unknowns)"));
        foreach (string outcome in queried.GroupBy(static r => r.Outcome).OrderByDescending(static g => g.Count()).Select(static g => $"{g.Key} {g.Count()}"))
        {
            Console.WriteLine($"- {outcome}");
        }
    }

    private static string Cell(int count, int total) => count == 0 ? "0" : Invariant($"{count} ({Share(count, total)})");

    private static string Share(int count, int total) => Invariant($"{100.0 * count / Math.Max(total, 1):F1}%");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
