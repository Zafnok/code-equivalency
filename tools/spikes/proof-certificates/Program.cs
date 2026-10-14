using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Frontend.CSharp;

namespace ProofCertificatesSpike;

/// <summary>
/// P1-026: can a solver Equivalent be checked by an independent proof checker? For every EQ001 result of a run's SARIF
/// whose <c>proofMethod</c> is <c>bounded</c> or <c>lockstep-induction</c>, it rebuilds the queries the proof rests on
/// (<see cref="ProofQueries"/>), writes each as an SMT-LIB 2 file, has cvc5 prove it with an Alethe proof and Carcara
/// check that proof (<see cref="Certifier"/>), and prints the tables the report quotes. Prints identities, statuses,
/// times, sizes and rule names; never a term or source text. The files hold names and constants from the code, so the
/// output folder belongs under <c>.corpus/</c>.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        Dictionary<string, string> flags = new(StringComparer.Ordinal);
        List<string> positional = [];
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--self-test")
            {
                flags["self-test"] = string.Empty;
            }
            else if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                flags[args[i][2..]] = args[++i];
            }
            else
            {
                positional.Add(args[i]);
            }
        }

        if (!flags.TryGetValue("cvc5", out string? cvc5) || !flags.TryGetValue("carcara", out string? carcara) || !flags.TryGetValue("rare", out string? rare))
        {
            Console.Error.WriteLine("usage: proof-certificates-spike --cvc5 <cvc5.exe> --carcara <carcara.exe> --rare <rewrites.eo> (--self-test <outDir> | <equiv.sarif> <legacy.sln> <modern.sln> <outDir>) [--threads n] [--only i,j] [--cvc5-ms n] [--check-ms n]");
            return 2;
        }

        int cvc5Ms = int.Parse(flags.GetValueOrDefault("cvc5-ms", "60000"), CultureInfo.InvariantCulture);
        int checkMs = int.Parse(flags.GetValueOrDefault("check-ms", "300000"), CultureInfo.InvariantCulture);
        Certifier certifier = new(cvc5, carcara, rare, cvc5Ms, checkMs);
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames) { ResourceLimit = config.ResourceLimit };
        if (flags.ContainsKey("self-test"))
        {
            return positional is [string selfTestDir] ? SelfTest.Run(certifier, options, selfTestDir) : 2;
        }

        if (positional is not [string sarif, string legacy, string modern, string outDir])
        {
            return 2;
        }

        int threads = int.Parse(flags.GetValueOrDefault("threads", "4"), CultureInfo.InvariantCulture);
        HashSet<int>? only = flags.TryGetValue("only", out string? list) ? [.. list.Split(',').Select(static n => int.Parse(n, CultureInfo.InvariantCulture))] : null;
        List<(string Identity, string Method)> results = Proofs(sarif);
        Dictionary<string, ProcedurePair> pairs = Load(legacy, modern);
        Directory.CreateDirectory(outDir);
        ConcurrentBag<Row> rows = [];
        int done = 0;
        Parallel.ForEach(
            results.Select(static (r, index) => (r.Identity, r.Method, Index: index)).Where(r => only?.Contains(r.Index) != false),
            new ParallelOptions { MaxDegreeOfParallelism = threads },
            r =>
            {
                Stopwatch clock = Stopwatch.StartNew();
                Row row = Measure(r.Index, r.Identity, r.Method, pairs.GetValueOrDefault(r.Identity), options, certifier, outDir);
                rows.Add(row);
                Console.Error.WriteLine($"[{Interlocked.Increment(ref done)}/{results.Count}] {clock.Elapsed.TotalSeconds:F0} s {row.Summary} {r.Identity}");
            });

        List<Row> ordered = [.. rows.OrderBy(static r => r.Index)];
        File.WriteAllLines(Path.Combine(outDir, "results.tsv"), ordered.SelectMany(static r => r.Lines()));
        Report.Print(results.Count, ordered);
        return 0;
    }

    private static Row Measure(int index, string identity, string method, ProcedurePair? pair, VerificationOptions options, Certifier certifier, string outDir)
    {
        if (pair is not { OldBody: { } old, NewBody: { } @new })
        {
            return new Row(index, identity, method, "pair not found at this commit", []);
        }

        (List<ProofQuery> queries, string? why) = ProofQueries.Of(method, old, @new, options);
        if (why is not null)
        {
            return new Row(index, identity, method, why, []);
        }

        List<(ProofQuery Query, Certificate? Certificate)> certified = [];
        for (int q = 0; q < queries.Count; q++)
        {
            string script = queries[q].Script;
            string file = Path.Combine(outDir, $"{index.ToString("D3", CultureInfo.InvariantCulture)}-{q}.smt2");
            File.WriteAllText(file, script);
            certified.Add((queries[q], certifier.Certify(file)));
        }

        return new Row(index, identity, method, null, certified);
    }

    /// <summary>Criterion 1: the EQ001 results proved <c>bounded</c> or <c>lockstep-induction</c>.</summary>
    private static List<(string Identity, string Method)> Proofs(string path)
    {
        // .corpus/ SARIF starts with a UTF-8 BOM, which JsonDocument.Parse(byte[]) rejects (P1-011); read it as text.
        using JsonDocument sarif = JsonDocument.Parse(File.ReadAllText(path));
        List<(string, string)> proofs = [];
        foreach (JsonElement result in sarif.RootElement.GetProperty("runs")[0].GetProperty("results").EnumerateArray())
        {
            if (result.GetProperty("ruleId").GetString() == "EQ001"
                && result.TryGetProperty("properties", out JsonElement properties)
                && properties.TryGetProperty("proofMethod", out JsonElement method)
                && method.GetString() is ProofQueries.Bounded or ProofQueries.Lockstep)
            {
                proofs.Add((result.GetProperty("partialFingerprints").GetProperty("procedureIdentity/v1").GetString()!, method.GetString()!));
            }
        }

        return proofs;
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

/// <summary>One result: why its proof was not rebuilt, or each of its queries with what cvc5 and Carcara made of it.</summary>
internal sealed record Row(int Index, string Identity, string Method, string? Why, List<(ProofQuery Query, Certificate? Certificate)> Queries)
{
    /// <summary>Every query has a proof Carcara accepts with no hole.</summary>
    public bool Certified => Why is null && Queries.Count > 0 && Queries.TrueForAll(static q => q.Certificate?.Carcara == Certifier.Valid);

    /// <summary>Every query has a proof Carcara accepts, some with holes.</summary>
    public bool CertifiedWithHoles => Why is null && Queries.Count > 0 && !Certified
        && Queries.TrueForAll(static q => q.Certificate?.Carcara is Certifier.Valid or Certifier.Holey);

    public string Summary => Why ?? string.Join(", ", Queries.Select(static q => $"{q.Query.Name}={Outcome(q)}"));

    public static string Outcome((ProofQuery Query, Certificate? Certificate) q) => q.Certificate switch
    {
        null => "not run",
        { Cvc5: not "unsat" } c => "cvc5 " + c.Cvc5,
        { ProofWritten: false } => "no Alethe proof",
        { } c => "Carcara " + c.Carcara,
    };

    public IEnumerable<string> Lines() => Why is not null
        ? [string.Join('\t', Index, Identity, Method, Why)]
        : Queries.Select(q => string.Join('\t', [
            Index.ToString(CultureInfo.InvariantCulture), Identity, Method, q.Query.Name, Outcome(q),
            q.Certificate?.Cvc5Ms.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            q.Certificate?.CheckMs.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            q.Certificate?.ProofBytes.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join("; ", q.Certificate?.Holes ?? []),
            q.Query.Printed,
            q.Certificate?.Detail ?? string.Empty]));
}

/// <summary>Prints the tables <c>docs/runs/&lt;date&gt;-proof-certificate-spike.md</c> quotes.</summary>
internal static class Report
{
    public static void Print(int results, List<Row> rows)
    {
        List<Row> rebuilt = [.. rows.Where(static r => r.Why is null)];
        List<(ProofQuery Query, Certificate? Certificate)> queries = [.. rebuilt.SelectMany(static r => r.Queries)];
        Console.WriteLine($"results {results}; proofs rebuilt {rebuilt.Count}; unsatisfiable queries {queries.Count}");

        Console.WriteLine();
        Console.WriteLine("## Results whose proof is not rebuilt");
        Console.WriteLine("| why | results |");
        Console.WriteLine("|---|---|");
        foreach (var group in rows.Where(static r => r.Why is not null).GroupBy(static r => $"{r.Method}: {r.Why}").OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Per query");
        Console.WriteLine("| proof method | query | printed | outcome | queries |");
        Console.WriteLine("|---|---|---|---|---|");
        foreach (var group in rebuilt
            .SelectMany(static r => r.Queries.Select(q => (r.Method, Name: q.Query.Name.StartsWith("step", StringComparison.Ordinal) ? "step" : q.Query.Name, q.Query.Printed, Outcome: Row.Outcome(q))))
            .GroupBy(static q => q)
            .OrderBy(static g => g.Key.Method, StringComparer.Ordinal).ThenBy(static g => g.Key.Printed, StringComparer.Ordinal).ThenByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key.Method} | {group.Key.Name} | {group.Key.Printed} | {group.Key.Outcome} | {group.Count()} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Outcome totals");
        Console.WriteLine("| outcome | queries |");
        Console.WriteLine("|---|---|");
        foreach (var group in queries.GroupBy(static q => Row.Outcome(q)).OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        List<Certificate> answered = [.. queries.Select(static q => q.Certificate).OfType<Certificate>().Where(static c => c.Cvc5 == "unsat")];
        List<Certificate> written = [.. answered.Where(static c => c.ProofWritten)];
        List<Certificate> checkedProofs = [.. written.Where(static c => c.Carcara is Certifier.Valid or Certifier.Holey or Certifier.Invalid or Certifier.MissingRule)];
        Console.WriteLine();
        Console.WriteLine("## Cost");
        Console.WriteLine("| measure | queries | median | largest |");
        Console.WriteLine("|---|---|---|---|");
        Console.WriteLine($"| cvc5 time to unsat with a proof, ms | {answered.Count} | {Median(answered.Select(static c => c.Cvc5Ms))} | {Max(answered.Select(static c => c.Cvc5Ms))} |");
        Console.WriteLine($"| Carcara time, ms | {checkedProofs.Count} | {Median(checkedProofs.Select(static c => c.CheckMs))} | {Max(checkedProofs.Select(static c => c.CheckMs))} |");
        Console.WriteLine($"| proof size, bytes | {written.Count} | {Median(written.Select(static c => c.ProofBytes))} | {Max(written.Select(static c => c.ProofBytes))} |");

        Console.WriteLine();
        Console.WriteLine("## Holes in proofs Carcara reports holey");
        Console.WriteLine("| hole | proofs |");
        Console.WriteLine("|---|---|");
        foreach (var group in written.Where(static c => c.Carcara == Certifier.Holey).SelectMany(static c => c.Holes.Select(static h => h[..h.LastIndexOf(" x", StringComparison.Ordinal)])).GroupBy(static h => h).OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key} | {group.Count()} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Why no proof, or why Carcara rejects it");
        Console.WriteLine("| outcome | detail | queries |");
        Console.WriteLine("|---|---|---|");
        foreach (var group in queries.Where(static q => q.Certificate is not { Carcara: Certifier.Valid or Certifier.Holey })
            .GroupBy(static q => (Outcome: Row.Outcome(q), Detail: Shape(q.Certificate?.Detail ?? string.Empty)))
            .OrderByDescending(static g => g.Count()))
        {
            Console.WriteLine($"| {group.Key.Outcome} | {group.Key.Detail} | {group.Count()} |");
        }

        Console.WriteLine();
        Console.WriteLine("## Pairs all of whose queries Carcara accepts with no hole");
        foreach (Row row in rebuilt.Where(static r => r.Certified).OrderBy(static r => r.Identity, StringComparer.Ordinal))
        {
            Console.WriteLine($"- `{row.Identity}` ({row.Method}, {row.Queries.Count} queries)");
        }

        Console.WriteLine();
        Console.WriteLine($"pairs certified with no hole: {rebuilt.Count(static r => r.Certified)} of {results}; accepted only with holes: {rebuilt.Count(static r => r.CertifiedWithHoles)}");
    }

    /// <summary>A detail with anything that may be a term or a name from the code cut off: the text up to the first quote, colon after a rule or parenthesis.</summary>
    private static string Shape(string detail)
    {
        int cut = detail.IndexOfAny(['(', '\'', '`', '@', '|']);
        string shape = (cut < 0 ? detail : detail[..cut]).Trim().TrimEnd(':', ',');
        return shape.Length > 120 ? shape[..120] : shape;
    }

    private static string Median(IEnumerable<long> values)
    {
        long[] sorted = [.. values.Order()];
        return sorted.Length == 0 ? "n/a" : sorted[sorted.Length / 2].ToString(CultureInfo.InvariantCulture);
    }

    private static string Max(IEnumerable<long> values) => values.DefaultIfEmpty(-1).Max() is var max && max < 0 ? "n/a" : max.ToString(CultureInfo.InvariantCulture);
}
