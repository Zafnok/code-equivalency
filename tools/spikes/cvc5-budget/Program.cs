using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Cvc5;
using Equiv.Verify.Z3;

namespace Cvc5Budget;

/// <summary>
/// P1-033 criterion 7: which <c>--rlimit</c> should cvc5 get? For every <c>timeout</c> Unknown of a run's SARIF it loads
/// the pair through the production frontend and verifies it with the production backend, once with no second solver and
/// once per limit given with cvc5 behind it, recording each script cvc5 was asked and how it ended. Prints identities,
/// verdicts and counts; never a model value or source text.
/// </summary>
internal static class Program
{
    private const string None = "no cvc5";

    public static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: cvc5-budget <equiv.sarif> <legacy.sln> <modern.sln> <outDir> --cvc5 <cvc5.exe> --limits a,b,c [--threads n] [--only index,...]");
            return 2;
        }

        int threads = 6;
        string? cvc5 = null;
        long[] limits = [];
        HashSet<int>? only = null;
        for (int i = 4; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--threads": threads = int.Parse(args[i + 1], CultureInfo.InvariantCulture); break;
                case "--cvc5": cvc5 = args[i + 1]; break;
                case "--limits": limits = [.. args[i + 1].Split(',').Select(static l => long.Parse(l, CultureInfo.InvariantCulture))]; break;
                case "--only": only = [.. args[i + 1].Split(',').Select(static n => int.Parse(n, CultureInfo.InvariantCulture))]; break;
                default: Console.Error.WriteLine($"unknown option {args[i]}"); return 2;
            }
        }

        if (cvc5 is null || limits.Length == 0)
        {
            Console.Error.WriteLine("--cvc5 and --limits are required");
            return 2;
        }

        List<string> identities = Timeouts(args[0]);
        Dictionary<string, ProcedurePair> pairs = Load(args[1], args[2]);
        Directory.CreateDirectory(args[3]);
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames) { ResourceLimit = config.ResourceLimit };
        string[] configurations = [None, .. limits.Select(static l => l.ToString(CultureInfo.InvariantCulture))];
        ConcurrentBag<Row> rows = [];
        int done = 0;
        Parallel.ForEach(
            identities.Select(static (identity, index) => (identity, index)).Where(item => only?.Contains(item.index) != false),
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            item =>
            {
                if (pairs.GetValueOrDefault(item.identity) is not { OldBody: { } old, NewBody: { } @new })
                {
                    rows.Add(new Row(item.index, item.identity, None, "pair not found at this commit", string.Empty, 0, []));
                    return;
                }

                foreach (string configuration in configurations)
                {
                    Recording? solver = configuration == None ? null : new Recording(new Cvc5Solver(cvc5) { ResourceLimit = long.Parse(configuration, CultureInfo.InvariantCulture) });
                    Stopwatch clock = Stopwatch.StartNew();
                    Verdict verdict = new Z3Backend().Verify(old, @new, options with { Solver = solver });
                    rows.Add(new Row(item.index, item.identity, configuration, Name(verdict), verdict.Ladder.FirstOrDefault(static s => s.Solver is not null)?.Solver?.Name ?? string.Empty, clock.ElapsedMilliseconds, solver?.Asks ?? []));
                }

                Console.Error.WriteLine($"[{Interlocked.Increment(ref done)}/{identities.Count}] {item.identity}");
            });

        List<Row> ordered = [.. rows.OrderBy(static r => r.Index).ThenBy(r => Array.IndexOf(configurations, r.Configuration))];
        File.WriteAllLines(
            Path.Combine(args[3], "results.tsv"),
            ordered.Select(static r => string.Join('\t', [r.Index.ToString(CultureInfo.InvariantCulture), r.Identity, r.Configuration, r.Verdict, r.Answered, r.Milliseconds.ToString(CultureInfo.InvariantCulture), .. r.Asks.Select(static a => $"{a.Ending}/{a.Milliseconds}ms")])));
        Report(identities.Count, ordered, configurations, options);
        return 0;
    }

    private static void Report(int timeouts, List<Row> rows, string[] configurations, VerificationOptions options)
    {
        Console.WriteLine($"timeout Unknowns in the SARIF: {timeouts}; verified at this commit: {rows.Count(static r => r.Configuration == None && r.Verdict != "pair not found at this commit")}");
        Console.WriteLine($"Z3: resourceLimit {options.ResourceLimit}, timeoutMs {options.TimeoutMs}; cvc5 wall-clock limit {options.TimeoutMs} ms");

        string[] endings = [Ask.Sat, Ask.Unsat, Ask.Resource, Ask.WallClock, Ask.Error, Ask.Other];
        Console.WriteLine();
        Console.WriteLine("## cvc5's answers, per limit");
        Console.WriteLine($"| `--rlimit` | scripts sent | {string.Join(" | ", endings)} | median ms of a sat or unsat | longest ms | total s |");
        Console.WriteLine($"|---|---|{string.Concat(Enumerable.Repeat("---|", endings.Length))}---|---|---|");
        foreach (string configuration in configurations.Skip(1))
        {
            List<Ask> asks = [.. rows.Where(r => r.Configuration == configuration).SelectMany(static r => r.Asks)];
            List<long> answered = [.. asks.Where(static a => a.Ending is Ask.Sat or Ask.Unsat).Select(static a => a.Milliseconds).Order()];
            Console.WriteLine($"| {configuration} | {asks.Count} | {string.Join(" | ", endings.Select(e => asks.Count(a => a.Ending == e)))} | {(answered.Count == 0 ? "n/a" : answered[answered.Count / 2].ToString(CultureInfo.InvariantCulture))} | {asks.Select(static a => a.Milliseconds).DefaultIfEmpty(0).Max()} | {asks.Sum(static a => a.Milliseconds) / 1000} |");
        }

        string[] verdicts = [.. rows.Where(static r => r.Verdict != "pair not found at this commit").Select(static r => r.Verdict).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Console.WriteLine();
        Console.WriteLine("## The pairs' verdicts, per configuration");
        Console.WriteLine($"| configuration | {string.Join(" | ", verdicts)} | results cvc5 answered a query of | pair seconds |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", verdicts.Length))}---|---|");
        foreach (string configuration in configurations)
        {
            List<Row> of = [.. rows.Where(r => r.Configuration == configuration && r.Verdict != "pair not found at this commit")];
            Console.WriteLine($"| {(configuration == None ? None : "`--rlimit` " + configuration)} | {string.Join(" | ", verdicts.Select(v => of.Count(r => r.Verdict == v)))} | {of.Count(static r => r.Answered.Length > 0)} | {of.Sum(static r => r.Milliseconds) / 1000} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Results whose verdict cvc5 changed");
        Console.WriteLine($"| procedure identity | {string.Join(" | ", configurations)} |");
        Console.WriteLine($"|---|{string.Concat(Enumerable.Repeat("---|", configurations.Length))}");
        foreach (IGrouping<string, Row> pair in rows.GroupBy(static r => r.Identity, StringComparer.Ordinal).OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            if (pair.Select(static r => r.Verdict).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                Console.WriteLine($"| `{pair.Key}` | {string.Join(" | ", configurations.Select(c => pair.First(r => r.Configuration == c).Verdict))} |");
            }
        }
    }

    private static string Name(Verdict verdict) => verdict switch
    {
        Equivalent => "Equivalent",
        Divergent => "Divergent",
        Unknown unknown => $"Unknown({unknown.Reason.ToString().ToLowerInvariant()})",
        _ => verdict.GetType().Name,
    };

    private static List<string> Timeouts(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        List<string> identities = [];
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (result.GetProperty("ruleId").GetString() == "EQ003"
                && result.TryGetProperty("properties", out JsonElement properties)
                && properties.TryGetProperty("unknownReason", out JsonElement reason)
                && reason.GetString() == "timeout")
            {
                identities.Add(result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!);
            }
        }

        return [.. identities.Distinct(StringComparer.Ordinal)];
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

/// <summary>One script cvc5 was asked: how it ended, and how long it took.</summary>
internal sealed record Ask(string Ending, long Milliseconds)
{
    public const string Sat = "sat";
    public const string Unsat = "unsat";
    public const string Resource = "unknown: resource limit";
    public const string WallClock = "unknown: wall-clock";
    public const string Error = "not read (error)";
    public const string Other = "other";
}

/// <summary>One pair under one configuration: its verdict, the solver its rung 1 step names, and the scripts cvc5 was asked.</summary>
internal sealed record Row(int Index, string Identity, string Configuration, string Verdict, string Answered, long Milliseconds, IReadOnlyList<Ask> Asks);

/// <summary>cvc5, with every script it is asked recorded by how it ended.</summary>
internal sealed class Recording(Cvc5Solver solver) : ISmtSolver
{
    public List<Ask> Asks { get; } = [];

    public string Name => solver.Name;

    public string Version => solver.Version;

    public SmtAnswer Ask(string script, TimeSpan limit)
    {
        Stopwatch clock = Stopwatch.StartNew();
        SmtAnswer answer = solver.Ask(script, limit);
        Asks.Add(new Ask(
            answer switch
            {
                SmtSat => Cvc5Budget.Ask.Sat,
                SmtUnsat => Cvc5Budget.Ask.Unsat,
                SmtUnknown { Reason: "unknown" } => Cvc5Budget.Ask.Resource,
                SmtUnknown { Reason: var reason } when reason.StartsWith("cvc5 interrupted by timeout", StringComparison.Ordinal) || reason.StartsWith("wall-clock limit", StringComparison.Ordinal) => Cvc5Budget.Ask.WallClock,
                SmtUnknown { Reason: var reason } when reason.StartsWith("(error", StringComparison.Ordinal) => Cvc5Budget.Ask.Error,
                _ => Cvc5Budget.Ask.Other,
            },
            clock.ElapsedMilliseconds));
        return answer;
    }
}
