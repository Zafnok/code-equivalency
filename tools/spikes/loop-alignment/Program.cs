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
using Equiv.Verify.Z3;

namespace LoopAlignmentSpike;

/// <summary>
/// P1-023: of a full run's <c>unaligned-loop</c> Unknowns, how many have a pairing of iterations that runs of the two
/// sides show? Reads the run's SARIF, lowers both solutions again, and for each such result records the loop forests,
/// rung 2's misalignment, what the loop bodies hold, and the first schedule that fits 200 runs of both sides in
/// <see cref="IrInterpreter"/> (<see cref="Schedules"/>). One invocation per run writes one row per result;
/// <c>--report</c> prints the tables over all of them. Identities, schedules and causes only.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--self-test"]:
                return SelfTest.Run();
            case ["--report", string changed, .. { Length: > 0 } files]:
                Report.Print(int.Parse(changed, CultureInfo.InvariantCulture), [.. files.SelectMany(File.ReadAllLines).Select(Row.Parse)]);
                return 0;
            case [string sarif, string legacy, string modern, string rows]:
                Measure(sarif, legacy, modern, rows);
                return 0;
            default:
                Console.Error.WriteLine("usage: loop-alignment-spike --self-test | <equiv.sarif> <legacy.sln> <modern.sln> <rows.tsv> | --report <changed pairs> <rows.tsv>...");
                return 2;
        }
    }

    private static void Measure(string sarif, string legacy, string modern, string rows)
    {
        List<(string Identity, string Detail)> results = ReadSarif(sarif);
        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        string run = Path.GetFileNameWithoutExtension(rows);
        List<string> lines = [];
        foreach ((string identity, string detail) in results)
        {
            ProcedurePair? pair = pairs.GetValueOrDefault(identity);
            Row row;
            try
            {
                row = Analyse(run, identity, detail, pair?.OldBody, pair?.NewBody);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                row = new Row(run, identity, detail, "n/a", "n/a", "n/a", "n/a", Schedule: null, "other: the interpreter refused the pair (" + e.GetType().Name + ")", 0, 0, 0, false);
            }

            Console.Error.WriteLine($"{row.Schedule ?? row.Cause}: {identity}");
            lines.Add(row.ToString());
        }

        File.WriteAllLines(rows, lines);
    }

    /// <summary>One result's row: the structure of the pair as the ladder sees it (shared fragments are calls), then the schedule search.</summary>
    public static Row Analyse(string run, string identity, string detail, IrProcedure? oldBody, IrProcedure? newBody)
    {
        if (oldBody is null || newBody is null)
        {
            return new Row(run, identity, detail, "n/a", "n/a", "n/a", "n/a", Schedule: null, "other: no such lowered pair at this commit", 0, 0, 0, false);
        }

        ProductEncoder.SharedFragments shared = ProductEncoder.ShareFragments(oldBody, newBody);
        (IrProcedure old, IrProcedure @new) = (shared.Old, shared.New);
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);

        // Only the constructor's alignment is read, which needs no ladder.
        string misalignment = new LockstepInduction(null!, old, @new, oldShape, newShape).Misalignment ?? "none: the loops align";
        string holds = Holds(oldBody, newBody);
        Row Structural(string cause) => new(run, identity, detail, misalignment, Forest(oldShape), Forest(newShape), holds, Schedule: null, cause, 0, 0, 0, false);
        if (oldShape.Loops.Length + newShape.Loops.Length == 0)
        {
            return Structural("other: neither side has a loop at this commit");
        }

        if (!oldShape.IsReducible || !newShape.IsReducible)
        {
            return Structural("other: a side's control flow is irreducible");
        }

        if (oldShape.Loops.Length != newShape.Loops.Length)
        {
            (IrProcedure fewer, IrProcedure more) = oldShape.Loops.Length < newShape.Loops.Length ? (old, @new) : (@new, old);
            bool linq = Callees(fewer).Except(Callees(more), StringComparer.Ordinal).Any(static c => c.StartsWith("System.Linq.", StringComparison.Ordinal));
            return Structural(linq ? "a loop on one side and a LINQ callee on the other" : "different number of loops");
        }

        if (Forest(oldShape) != Forest(newShape))
        {
            return Structural("other: the loops nest differently");
        }

        Finding finding = Schedules.Search(old, @new, oldShape, newShape);
        return new Row(run, identity, detail, misalignment, Forest(oldShape), Forest(newShape), holds, finding.Schedule?.ToString(), finding.Cause ?? "n/a", finding.Usable, finding.Reaching, finding.MostVisits, finding.OutcomesAgree);
    }

    /// <summary>The loop count and each loop's parent in pre-order, <c>.</c> for a top-level loop.</summary>
    private static string Forest(IrLoopAnalysis shape)
    {
        List<IrBlockId> headers = [.. shape.Loops.Select(static l => l.Header)];
        return shape.Loops.Length.ToString(CultureInfo.InvariantCulture) + " (" + string.Join(' ', shape.Loops.Select(l => l.Parent is null ? "." : headers.IndexOf(l.Parent).ToString(CultureInfo.InvariantCulture))) + ")";
    }

    /// <summary>Which of a call, an <see cref="IrPure"/> and an opaque some loop body of either side holds, as lowered.</summary>
    private static string Holds(params IrProcedure[] sides)
    {
        List<IrInstruction> inLoops = [.. sides.SelectMany(static side =>
        {
            HashSet<IrBlockId> blocks = [.. IrLoopAnalysis.Of(side).Loops.SelectMany(static l => l.Blocks)];
            return side.Blocks.Where(b => blocks.Contains(b.Id)).SelectMany(static b => b.Instructions);
        })];
        string[] kinds = [inLoops.OfType<IrCall>().Any() ? "call" : "", inLoops.OfType<IrPure>().Any() ? "pure" : "", inLoops.OfType<IrOpaque>().Any() ? "opaque" : ""];
        return string.Join('+', kinds.Where(static k => k.Length > 0).DefaultIfEmpty("nothing"));
    }

    private static IEnumerable<string> Callees(IrProcedure side) => side.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static c => c.Callee.Value);

    /// <summary>Each <c>unaligned-loop</c> EQ003 result's identity and the detail of its lockstep rung.</summary>
    private static List<(string Identity, string Detail)> ReadSarif(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        List<(string, string)> results = [];
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (!string.Equals(result.GetProperty("ruleId").GetString(), "EQ003", StringComparison.Ordinal)
                || !result.TryGetProperty("properties", out JsonElement properties)
                || !properties.TryGetProperty("unknownReason", out JsonElement reason)
                || !string.Equals(reason.GetString(), "unaligned-loop", StringComparison.Ordinal))
            {
                continue;
            }

            string detail = properties.TryGetProperty("ladderTrace", out JsonElement ladder)
                ? ladder.EnumerateArray()
                    .Where(static s => string.Equals(s.GetProperty("rung").GetString(), "lockstep-induction", StringComparison.Ordinal))
                    .Select(static s => s.GetProperty("detail").GetString()!)
                    .FirstOrDefault("no lockstep step")
                : "no ladder trace";
            results.Add((result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!, detail));
        }

        return results;
    }
}

/// <summary>One <c>unaligned-loop</c> Unknown. <see cref="Schedule"/> is the fitting schedule, or null with <see cref="Cause"/> saying why none fits.</summary>
internal sealed record Row(string Run, string Identity, string Detail, string Misalignment, string OldForest, string NewForest, string Holds, string? Schedule, string Cause, int Usable, int Reaching, int MostVisits, bool OutcomesAgree)
{
    public static Row Parse(string line)
    {
        string[] f = line.Split('\t');
        return new Row(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7].Length == 0 ? null : f[7], f[8], int.Parse(f[9], CultureInfo.InvariantCulture), int.Parse(f[10], CultureInfo.InvariantCulture), int.Parse(f[11], CultureInfo.InvariantCulture), bool.Parse(f[12]));
    }

    public override string ToString() =>
        string.Join('\t', Run, Identity, Detail, Misalignment, OldForest, NewForest, Holds, Schedule ?? "", Cause, Usable.ToString(CultureInfo.InvariantCulture), Reaching.ToString(CultureInfo.InvariantCulture), MostVisits.ToString(CultureInfo.InvariantCulture), OutcomesAgree);
}

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-loop-alignment-spike.md</c> quotes.</summary>
internal static partial class Report
{
    private const string Lockstep = "1:1, no offset";

    public static void Print(int changed, List<Row> rows)
    {
        int total = rows.Count;
        string Cells(int count) => string.Create(CultureInfo.InvariantCulture, $"{count} | {100.0 * count / Math.Max(total, 1):F1}% | {100.0 * count / changed:F1}%");
        void Table(string title, string key, IEnumerable<IGrouping<string, Row>> groups)
        {
            Console.WriteLine();
            Console.WriteLine($"## {title}");
            Console.WriteLine($"| {key} | results | share of unaligned-loop | share of changed pairs |");
            Console.WriteLine("|---|---|---|---|");
            foreach (IGrouping<string, Row> group in groups.OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"| {group.Key} | {Cells(group.Count())} |");
            }
        }

        List<Row> fitting = [.. rows.Where(static r => r.Schedule is not null)];
        List<Row> others = [.. fitting.Where(static r => r.Schedule != Lockstep)];
        Console.WriteLine($"unaligned-loop results {total}; changed pairs {changed.ToString(CultureInfo.InvariantCulture)}; by run: {string.Join(", ", rows.GroupBy(static r => r.Run).Select(static g => $"{g.Key} {g.Count()}"))}");
        Console.WriteLine($"with a fitting schedule: {Cells(fitting.Count)}");
        Console.WriteLine($"with a fitting schedule other than {Lockstep} (criterion 4): {Cells(others.Count)}");
        Console.WriteLine($"fitting pairs whose outcomes and final by-ref values also agreed on every run: {fitting.Count(static r => r.OutcomesAgree)}");
        Table("Pairs with a fitting schedule, by schedule", "schedule", fitting.GroupBy(static r => r.Schedule!));
        Table("Pairs with none, by cause", "cause", rows.Where(static r => r.Schedule is null).GroupBy(static r => r.Cause));
        Table("Rung 2's alignment at this commit", "misalignment", rows.GroupBy(static r => Scrub(r.Misalignment)));
        Table("The lockstep rung's detail in the run", "detail", rows.GroupBy(static r => Scrub(r.Detail)));
        Table("What the loop bodies hold", "holds", rows.GroupBy(static r => r.Holds));
        Table("Outcome by what the loop bodies hold", "holds, outcome", rows.GroupBy(static r => r.Holds + ", " + (r.Schedule is null ? "none" : r.Schedule == Lockstep ? "lockstep" : "other schedule")));

        Console.WriteLine();
        Console.WriteLine("## Every result");
        Console.WriteLine("| run | procedure | loops (legacy, modern) | holds | schedule or cause | usable runs | reaching a loop | most header visits |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (Row row in rows.OrderBy(static r => r.Schedule is null).ThenBy(static r => r.Schedule ?? r.Cause, StringComparer.Ordinal).ThenBy(static r => r.Run, StringComparer.Ordinal).ThenBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| {row.Run} | `{row.Identity}` | {row.OldForest}, {row.NewForest} | {row.Holds} | {row.Schedule ?? row.Cause} | {row.Usable} | {row.Reaching} | {row.MostVisits} |");
        }
    }

    /// <summary>A rung detail without its variable names and loop numbers, so that equal reasons group and no source name is printed.</summary>
    private static string Scrub(string detail) => Numbers().Replace(Names().Replace(detail, string.Empty), "N").Trim();

    [GeneratedRegex(@"\s*\(\[.*\]\)")]
    private static partial Regex Names();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Numbers();
}
