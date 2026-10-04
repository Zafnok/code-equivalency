using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp;

namespace DivergentCount;

/// <summary>
/// P1-027: on what share of its parameter space does a Divergent pair diverge? Reads a full run's EQ002 results (and the
/// EQ002 rows of the P2-047 audit, when given), lowers both solutions again, classifies each pair's source parameters
/// (<see cref="Inputs"/>), counts each countable pair (<see cref="Counter"/>) and writes one tab-separated row per result.
/// <c>--report</c> prints the tables the report quotes from those rows. Identities and counts only, never a
/// counterexample value or source text.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args is ["--report", .. string[] files] && files.Length > 0)
        {
            Report.Print([.. files.SelectMany(File.ReadAllLines).Select(Row.Parse)]);
            return 0;
        }

        if (args.Length is not (5 or 6))
        {
            Console.Error.WriteLine("usage: divergent-count-spike --self-test | --report <rows.tsv>... | <slug> <equiv.sarif> <legacy.sln> <modern.sln> <rows.tsv> [<divergent-audit.md>]");
            return 2;
        }

        string slug = args[0];
        HashSet<string> reported = ReadSarif(args[1]);
        Dictionary<string, string> audited = args.Length == 6 ? ReadAudit(args[5]) : [];
        Dictionary<string, ProcedurePair> pairs = Load(args[2], args[3]);
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        List<Row> rows = [];
        int seed = 0;
        foreach (string identity in reported.Union(audited.Keys).Order(StringComparer.Ordinal))
        {
            Row row = new(slug, identity, reported.Contains(identity), audited.GetValueOrDefault(identity, string.Empty), "pair-not-found", 0, string.Empty, double.NaN, false, 0, 0);
            if (pairs.GetValueOrDefault(identity) is { OldBody: { } old, NewBody: { } @new })
            {
                row = Measure(row, old, @new, options, seed++);
            }

            Console.Error.WriteLine($"{row.Class} {row.Outcome} {identity} in {row.Milliseconds} ms");
            rows.Add(row);
        }

        File.WriteAllLines(args[4], rows.Select(static r => r.ToString()));
        return 0;
    }

    private static Row Measure(Row row, IrProcedure old, IrProcedure @new, VerificationOptions options, int seed)
    {
        // Rung 1 does not encode these (LoopLadder.Bounded), so no Divergent comes from them at this commit.
        (IrProcedure sharedOld, IrProcedure sharedNew, _) = Equiv.Verify.Z3.ProductEncoder.ShareFragments(old, @new);
        if (!IrLoopAnalysis.Of(sharedOld).IsReducible || !IrLoopAnalysis.Of(sharedNew).IsReducible
            || (IrUnroller.InliningObstacle(sharedOld) ?? IrUnroller.InliningObstacle(sharedNew)) is not null)
        {
            return row with { Class = "not-encodable" };
        }

        Classification classification = Inputs.Classify(IrUnroller.Unroll(sharedOld, options.Bound), IrUnroller.Unroll(sharedNew, options.Bound));
        if (!classification.Countable)
        {
            return row with { Class = classification.Blocker! };
        }

        Count count = new Counter(options, seed).Run(old, @new, classification);
        return row with { Class = "countable", Bits = count.Bits, Outcome = count.Outcome, Log2Share = count.Log2Share, Exact = count.Exact, Checks = count.Checks, Milliseconds = count.Milliseconds };
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

    /// <summary>The procedure identity of every EQ002 result.</summary>
    private static HashSet<string> ReadSarif(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        return
        [
            .. sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray()
                .Where(static r => string.Equals(r.GetProperty("ruleId").GetString(), "EQ002", StringComparison.Ordinal))
                .Select(static r => r.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!),
        ];
    }

    /// <summary>The Git Extensions EQ002 rows of the P2-047 audit's appendix: procedure identity to classification.</summary>
    private static Dictionary<string, string> ReadAudit(string path)
    {
        Regex row = new(@"^\| \d+ \| Git Extensions \| EQ002 \| `` (?<identity>.+?) `` \| `[0-9a-f]+` \| (?<class>confirmed|false positive|undetermined) \|", RegexOptions.CultureInvariant);
        return File.ReadLines(path).Select(l => row.Match(l)).Where(static m => m.Success).ToDictionary(static m => m.Groups["identity"].Value, static m => m.Groups["class"].Value, StringComparer.Ordinal);
    }
}

/// <summary>One result: where it came from, its classification, and its count when it is countable.</summary>
internal sealed record Row(string Slug, string Identity, bool Reported, string Audit, string Class, int Bits, string Outcome, double Log2Share, bool Exact, int Checks, long Milliseconds)
{
    public static Row Parse(string line)
    {
        string[] f = line.Split('\t');
        return new Row(f[0], f[1], bool.Parse(f[2]), f[3], f[4], int.Parse(f[5], CultureInfo.InvariantCulture), f[6], double.Parse(f[7], CultureInfo.InvariantCulture), bool.Parse(f[8]), int.Parse(f[9], CultureInfo.InvariantCulture), long.Parse(f[10], CultureInfo.InvariantCulture));
    }

    public override string ToString() =>
        string.Join('\t', Slug, Identity, Reported, Audit, Class, Bits.ToString(CultureInfo.InvariantCulture), Outcome, Log2Share.ToString("R", CultureInfo.InvariantCulture), Exact, Checks.ToString(CultureInfo.InvariantCulture), Milliseconds.ToString(CultureInfo.InvariantCulture));

    public bool Counted => string.Equals(Outcome, Counter.Counted, StringComparison.Ordinal);

    /// <summary>
    /// The decade of a counted share: no parameter at all, a single assignment (of more than one), more than half, or
    /// eight binary orders at a time.
    /// </summary>
    public string Decade
    {
        get
        {
            if (Bits == 0)
            {
                return "1 (no source parameter)";
            }

            if (Exact && Bits > 1 && Math.Abs(Log2Share + Bits) < 1e-9)
            {
                return "a single input";
            }

            if (Log2Share > -1)
            {
                return "1 (more than half)";
            }

            int band = (int)Math.Ceiling(-Log2Share / 8);
            return string.Create(CultureInfo.InvariantCulture, $"2^-{((band - 1) * 8) + 1}..2^-{band * 8}");
        }
    }
}

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-divergent-count-spike.md</c> quotes.</summary>
internal static class Report
{
    public static void Print(List<Row> rows)
    {
        List<Row> reported = [.. rows.Where(static r => r.Reported)];
        Console.WriteLine("## EQ002 results by run");
        Console.WriteLine("| run | EQ002 | countable | counted in time | not countable | pair not found or not encodable |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (IGrouping<string, Row> run in reported.GroupBy(static r => r.Slug).Append(reported.GroupBy(static _ => "all").Single()))
        {
            List<Row> all = [.. run];
            int countable = all.Count(static r => r.Class == "countable");
            int missing = all.Count(static r => r.Class is "pair-not-found" or "not-encodable");
            Console.WriteLine($"| {run.Key} | {all.Count} | {Cell(countable, all.Count)} | {Cell(all.Count(static r => r.Counted), all.Count)} | {Cell(all.Count - countable - missing, all.Count)} | {missing} |");
        }

        Table("First blocker of the results that are not countable", reported.Where(static r => r.Class != "countable"), static r => r.Class, reported.Count);
        Table("Outcome of the countable results", reported.Where(static r => r.Class == "countable"), static r => r.Outcome, reported.Count);
        Table("Counted shares by decade", reported.Where(static r => r.Counted), static r => r.Decade, reported.Count);
        Table("Counted results by how they were counted", reported.Where(static r => r.Counted), static r => r.Exact ? "enumerated (exact)" : "hashed (within tolerance)", reported.Count);

        List<Row> audited = [.. rows.Where(static r => r.Audit.Length > 0)];
        Console.WriteLine();
        Console.WriteLine("## Audited EQ002 results (P2-047), by classification");
        Console.WriteLine("| classification | results | countable | counted | not Divergent at this commit | decades of the counted |");
        Console.WriteLine("|---|---|---|---|---|---|");
        foreach (IGrouping<string, Row> group in audited.GroupBy(static r => r.Audit).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            List<Row> all = [.. group];
            string decades = string.Join("; ", all.Where(static r => r.Counted).GroupBy(static r => r.Decade).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key}: {g.Count()}"));
            Console.WriteLine($"| {group.Key} | {all.Count} | {all.Count(static r => r.Class == "countable")} | {all.Count(static r => r.Counted)} | {all.Count(static r => r.Outcome == Counter.NotDivergent)} | {(decades.Length == 0 ? "none" : decades)} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Every countable result");
        Console.WriteLine("| run | procedure | in the run's EQ002 | audit | bits | outcome | log2 share | solver checks | seconds |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|");
        foreach (Row row in rows.Where(static r => r.Class == "countable").OrderBy(static r => r.Slug, StringComparer.Ordinal).ThenBy(static r => r.Identity, StringComparer.Ordinal))
        {
            string share = row.Counted ? row.Log2Share.ToString("F1", CultureInfo.InvariantCulture) + (row.Exact ? " (exact)" : string.Empty) : "n/a";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {row.Slug} | `` {row.Identity} `` | {(row.Reported ? "yes" : "no")} | {(row.Audit.Length == 0 ? "n/a" : row.Audit)} | {row.Bits} | {row.Outcome} | {share} | {row.Checks} | {row.Milliseconds / 1000.0:F1} |"));
        }
    }

    private static void Table(string title, IEnumerable<Row> rows, Func<Row, string> key, int total)
    {
        Console.WriteLine();
        Console.WriteLine($"## {title}");
        Console.WriteLine("| | results | share of EQ002 results |");
        Console.WriteLine("|---|---|---|");
        foreach (IGrouping<string, Row> group in rows.GroupBy(key).OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} | {Share(group.Count(), total)} |");
        }
    }

    private static string Cell(int count, int total) => $"{count} ({Share(count, total)})";

    private static string Share(int count, int total) => (100.0 * count / Math.Max(total, 1)).ToString("F1", CultureInfo.InvariantCulture) + "%";
}
