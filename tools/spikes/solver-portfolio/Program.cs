using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace SolverPortfolioSpike;

/// <summary>
/// P1-025: would a second solver decide the queries Z3 gives up on? For every <c>timeout</c> Unknown of a run's SARIF it
/// builds the pair as the P1-019 spike does, asks rung 1's queries with the production solver and limits, writes the one
/// Z3 gives up on as an SMT-LIB 2 file, runs that file with each solver given, and reads a satisfiable answer's model
/// back through the replay. Prints identities, statuses and counts; never a model value or source text. The files
/// themselves hold names and constants from the code, so the output folder belongs under <c>.corpus/</c>.
/// </summary>
internal static class Program
{
    private static readonly string[] HornReasons = ["chc-timeout", "chc-spurious", "no-invariant"];

    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        if (args is ["--horn", .. string[] runs] && runs.Length > 0)
        {
            return Horn(runs);
        }

        if (args is ["--unwrap", string from, string to])
        {
            // Diagnosis: an exported file with each seq.++ of one argument written as that argument.
            File.WriteAllText(to, new SmtFile("n/a", File.ReadAllText(from), new Dictionary<string, string>(), []).Unwrapped(wrap: false));
            return 0;
        }

        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: solver-portfolio-spike --self-test | --horn <equiv.sarif>... | <equiv.sarif> <legacy.sln> <modern.sln> <outDir> [--threads n] [--queries <results.tsv>] [--solver name=path[|option]...]...");
            return 2;
        }

        int threads = 4;
        Dictionary<string, string>? known = null;
        List<(string Name, string Path)> solvers = [];
        for (int i = 4; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--threads")
            {
                threads = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--queries")
            {
                // An earlier pass's results.tsv: its answer to which query Z3 gives up on is taken, and Z3 is not asked again.
                known = File.ReadLines(args[i + 1]).Select(static l => l.Split('\t')).ToDictionary(static c => c[1], static c => c[2], StringComparer.Ordinal);
            }
            else
            {
                string[] solver = args[i + 1].Split('=', 2);
                solvers.Add((solver[0], solver[1]));
            }
        }

        List<string> identities = Timeouts(args[0]);
        Dictionary<string, ProcedurePair> pairs = Load(args[1], args[2]);
        Directory.CreateDirectory(args[3]);
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames);
        ConcurrentBag<Row> rows = [];
        int done = 0;
        Parallel.ForEach(
            identities.Select(static (identity, index) => (identity, index)),
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            item =>
            {
                Stopwatch clock = Stopwatch.StartNew();
                Row row = Measure(item.identity, item.index, pairs.GetValueOrDefault(item.identity), options, args[3], solvers, known?.GetValueOrDefault(item.identity));
                rows.Add(row);
                Console.Error.WriteLine($"[{Interlocked.Increment(ref done)}/{identities.Count}] {clock.Elapsed.TotalSeconds:F0} s {row.Query ?? row.Why} {item.identity}");
            });

        List<Row> ordered = [.. rows.OrderBy(static r => r.Index)];
        File.WriteAllLines(
            Path.Combine(args[3], "results.tsv"),
            ordered.Select(r => string.Join('\t', [r.Index.ToString(CultureInfo.InvariantCulture), r.Identity, r.Query ?? r.Why, r.Logic, string.Join(',', r.Z3Only), .. r.Answers.Select(static a => $"{a.Solver}={a.Status}/{a.Milliseconds}ms/{a.ReadBack}/{a.Error}")])));
        Report.Print(identities.Count, ordered, [.. solvers.SelectMany(static s => (string[])[s.Name, s.Name + Report.UnwrappedSuffix])], options.TimeoutMs);
        return 0;
    }

    private static Row Measure(string identity, int index, ProcedurePair? pair, VerificationOptions options, string outDir, List<(string Name, string Path)> solvers, string? known)
    {
        if (pair is not { OldBody: { } old, NewBody: { } @new })
        {
            return new Row(index, identity, null, "pair not found at this commit");
        }

        (Rung1? rung, string? notApplicable) = Rung1.Prepare(old, @new, options);
        if (rung is null)
        {
            return new Row(index, identity, null, notApplicable);
        }

        using Context context = new();
        ProductEncoding encoding = rung.Encode(context, options);
        (string? query, string? why) = known switch
        {
            null => rung.FirstUnknown(context, encoding, options),
            Rung1.Divergence or Rung1.Opaque or Rung1.Bound => (known, null),
            _ => (null, known),
        };
        if (query is null)
        {
            return new Row(index, identity, null, why);
        }

        SmtFile file = SmtFile.Of(context, encoding, query);
        string path = Path.Combine(outDir, index.ToString("D3", CultureInfo.InvariantCulture) + ".smt2");
        File.WriteAllText(path, file.Script());
        List<Answer> answers = [];
        if (file.Z3Only.Count == 0)
        {
            // As Z3 prints it first; a solver that cannot read that gets the file with unary seq.++ unwrapped.
            string unwrapped = Path.ChangeExtension(path, ".u.smt2");
            File.WriteAllText(unwrapped, file.Unwrapped());
            foreach ((string name, string exe) in solvers)
            {
                Answer asPrinted = Ask(name, exe, path);
                answers.Add(asPrinted);
                answers.Add(asPrinted.Status == "error" ? Ask(name + Report.UnwrappedSuffix, exe, unwrapped) : asPrinted with { Solver = name + Report.UnwrappedSuffix });
            }

            Answer Ask(string name, string exe, string script)
            {
                (string status, long milliseconds, string output, string error) = External.Run(exe, script, options.TimeoutMs);
                string readBack = status == "sat" ? ReadBack.Run(context, encoding, options, rung, query, file, output) : string.Empty;
                return new Answer(name, status, milliseconds, readBack, error);
            }
        }

        return new Row(index, identity, query, null) { Logic = file.Logic, Z3Only = file.Z3Only, Answers = answers };
    }

    /// <summary>Criterion 4: the rung 4 Unknowns of each run, by reason.</summary>
    private static int Horn(string[] runs)
    {
        int total = 0;
        Console.WriteLine($"| run | {string.Join(" | ", HornReasons)} |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", HornReasons.Length))}");
        foreach (string run in runs)
        {
            List<string> reasons = [.. Unknowns(run).Select(static u => u.Reason)];
            int[] counts = [.. HornReasons.Select(h => reasons.Count(r => r == h))];
            total += counts.Sum();
            // .corpus/pairs/<slug>/runs/<run>/equiv.sarif
            string[] parts = Path.GetFullPath(run).Split(Path.DirectorySeparatorChar);
            Console.WriteLine($"| `{(parts.Length > 3 ? parts[^4] : run)}` `{(parts.Length > 1 ? parts[^2] : string.Empty)}` ({reasons.Count} Unknowns) | {string.Join(" | ", counts)} |");
        }

        Console.WriteLine();
        Console.WriteLine($"rung 4 Unknowns over {runs.Length} runs: {total}");
        return 0;
    }

    private static List<string> Timeouts(string sarif) =>
        [.. Unknowns(sarif).Where(static u => u.Reason == "timeout").Select(static u => u.Identity).Distinct(StringComparer.Ordinal)];

    private static IEnumerable<(string Identity, string Reason)> Unknowns(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (result.GetProperty("ruleId").GetString() == "EQ003"
                && result.TryGetProperty("properties", out JsonElement properties)
                && properties.TryGetProperty("unknownReason", out JsonElement reason))
            {
                yield return (result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!, reason.GetString()!);
            }
        }
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
}

/// <summary>One solver's answer on one exported file. <c>Error</c> stays in <c>results.tsv</c>; the report prints counts only.</summary>
internal sealed record Answer(string Solver, string Status, long Milliseconds, string ReadBack, string Error);

/// <summary>One <c>timeout</c> Unknown: the rung 1 query Z3 gives up on, or why none was exported.</summary>
internal sealed record Row(int Index, string Identity, string? Query, string? Why)
{
    public string Logic { get; init; } = string.Empty;

    public IReadOnlyList<string> Z3Only { get; init; } = [];

    public IReadOnlyList<Answer> Answers { get; init; } = [];

    public bool Exported => Query is not null && Z3Only.Count == 0;
}

/// <summary>Runs a solver on a file as a process, for at most the wall-clock time Z3 had.</summary>
internal static class External
{
    public static (string Status, long Milliseconds, string Output, string Error) Run(string exe, string file, int timeoutMs)
    {
        // <path>|<option>|...: the solver's options come before the file.
        string[] command = exe.Split('|');
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false },
        };
        foreach (string argument in command.Skip(1).Append(file))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Stopwatch clock = Stopwatch.StartNew();
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return ("timeout", clock.ElapsedMilliseconds, string.Empty, string.Empty);
        }

        long took = clock.ElapsedMilliseconds;
        string text = output.Result;
        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return first is "sat" or "unsat" or "unknown"
            ? (first, took, text, string.Empty)
            : ("error", took, string.Empty, OneLine(first.Length > 0 ? first : error.Result));
    }

    private static string OneLine(string text)
    {
        string line = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 200 ? line[..200] : line;
    }
}

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-solver-portfolio.md</c> quotes.</summary>
internal static class Report
{
    public const string UnwrappedSuffix = " (unary seq.++ unwrapped)";

    private static readonly string[] Statuses = ["unsat", "sat", "unknown", "timeout", "error"];

    public static void Print(int timeouts, List<Row> rows, List<string> solvers, int timeoutMs)
    {
        List<Row> written = [.. rows.Where(static r => r.Query is not null)];
        List<Row> exported = [.. rows.Where(static r => r.Exported)];
        Console.WriteLine($"timeout Unknowns {timeouts}; rung 1 query Z3 gives up on {written.Count}; exported {exported.Count}");

        Console.WriteLine();
        Console.WriteLine("## Export");
        Console.WriteLine("| outcome | timeout Unknowns |");
        Console.WriteLine("|---|---|");
        foreach (var group in exported.GroupBy(static r => $"exported: `{r.Query}` query, logic {r.Logic}").OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        foreach (var group in written.Where(static r => !r.Exported).GroupBy(static r => $"not exported: Z3-only operator {string.Join(", ", r.Z3Only.Select(static o => $"`{o}`"))}").OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        foreach (var group in rows.Where(static r => r.Query is null).GroupBy(static r => $"no query: {r.Why}").OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        if (solvers.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"## Answers on the {exported.Count} exported files, {timeoutMs} ms each");
        Console.WriteLine($"| solver | {string.Join(" | ", Statuses)} | median ms of an answer |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", Statuses.Length))}---|");
        foreach (string solver in solvers)
        {
            List<Answer> answers = [.. Of(exported, solver)];
            List<long> answered = [.. answers.Where(static a => a.Status is "sat" or "unsat").Select(static a => a.Milliseconds).Order()];
            Console.WriteLine($"| {solver} | {string.Join(" | ", Statuses.Select(s => answers.Count(a => a.Status == s)))} | {(answered.Count == 0 ? "n/a" : answered[answered.Count / 2].ToString(CultureInfo.InvariantCulture))} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Satisfiable answers read back (criterion 3)");
        Console.WriteLine("| solver | query | read back as | results |");
        Console.WriteLine("|---|---|---|---|");
        foreach (string solver in solvers)
        {
            foreach (var group in exported
                .SelectMany(r => r.Answers.Where(a => a.Solver == solver && a.Status == "sat").Select(a => (r.Query, a.ReadBack)))
                .GroupBy(static a => a)
                .OrderByDescending(static g => g.Count()))
            {
                Console.WriteLine($"| {solver} | `{group.Key.Query}` | {group.Key.ReadBack} | {group.Count()} |");
            }
        }

        Console.WriteLine();
        Console.WriteLine("## Per result");
        Console.WriteLine($"| procedure identity | query | {string.Join(" | ", solvers)} |");
        Console.WriteLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", solvers.Count))}");
        foreach (Row row in exported.OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"| `{row.Identity}` | `{row.Query}` | {string.Join(" | ", solvers.Select(s => Cell(row.Answers.First(a => a.Solver == s))))} |");
        }

        // An unsatisfiable answer proves the query; a satisfiable one refutes the pair only when its model replays to a Divergent.
        int proved = exported.Count(static r => r.Answers.Any(static a => a.Status == "unsat"));
        int refuted = exported.Count(static r => r.Answers.Any(static a => a.ReadBack == ReadBack.Divergent));
        int abstraction = exported.Count(static r => r.Answers.Any(static a => a.ReadBack == ReadBack.Abstraction) && !r.Answers.Any(static a => a.ReadBack == ReadBack.Divergent));
        Console.WriteLine();
        Console.WriteLine($"timeout Unknowns some other solver proves or refutes: {exported.Count(static r => r.Answers.Any(static a => a.Status == "unsat" || a.ReadBack == ReadBack.Divergent))} of {timeouts} (query proved unsatisfiable {proved}, refuted by a replayed model {refuted}); a further {abstraction} become Unknown(abstraction)");
    }

    private static IEnumerable<Answer> Of(List<Row> rows, string solver) => rows.SelectMany(r => r.Answers.Where(a => a.Solver == solver));

    private static string Cell(Answer answer) => answer.Status == "sat" ? $"sat: {answer.ReadBack}" : answer.Status;
}
