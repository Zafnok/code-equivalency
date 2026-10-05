using System.Diagnostics;
using System.Text;

namespace Cvc5PartitioningSpike;

/// <summary>One cvc5 process: its answer, its wall-clock and processor time, and what it printed.</summary>
internal sealed record Ran(string Status, long WallMs, long CpuMs, string Output, string Error);

/// <summary>
/// One query solved through partitions. <c>Status</c> is <c>sat</c>, <c>unsat</c>, <c>undecided</c> (partitions were made
/// and they do not settle it), <c>no-partitions</c> (cvc5 wrote none in its time) or <c>error</c>.
/// </summary>
internal sealed record Solved(string Status, int Partitions, string Cover, string Own, string Parts, long WallMs, long CpuMs, string Values);

/// <summary>Runs cvc5 as a process for at most a wall-clock time, or until it is told to stop.</summary>
internal static class Cvc5
{
    public const string Stopped = "stopped";
    public const string Timeout = "timeout";

    public static async Task<Ran> RunAsync(string exe, IEnumerable<string> arguments, int timeoutMs, CancellationToken stop)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        Stopwatch clock = Stopwatch.StartNew();
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(timeoutMs);
        string? ended = null;
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ended = stop.IsCancellationRequested ? Stopped : Timeout;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        long wall = clock.ElapsedMilliseconds;
        long cpu = (long)process.TotalProcessorTime.TotalMilliseconds;
        string text = await output.ConfigureAwait(false);
        string complaint = await error.ConfigureAwait(false);
        if (ended is not null)
        {
            return new Ran(ended, wall, cpu, string.Empty, string.Empty);
        }

        string first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return first is "sat" or "unsat" or "unknown"
            ? new Ran(first, wall, cpu, text, string.Empty)
            : new Ran("error", wall, cpu, string.Empty, OneLine(first.Length > 0 ? first : complaint));
    }

    private static string OneLine(string text)
    {
        string line = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 200 ? line[..200] : line;
    }
}

/// <summary>The processes the box may run at once. A query's partitions take their places together and run side by side.</summary>
internal sealed class Slots
{
    private readonly object _gate = new();
    private readonly int _total;
    private int _free;

    public Slots(int total) => (_total, _free) = (total, total);

    public int Acquire(int wanted)
    {
        int taken = Math.Min(wanted, _total);
        lock (_gate)
        {
            while (_free < taken)
            {
                Monitor.Wait(_gate);
            }

            _free -= taken;
        }

        return taken;
    }

    public void Release(int taken)
    {
        lock (_gate)
        {
            _free += taken;
            Monitor.PulseAll(_gate);
        }
    }
}

/// <summary>
/// Criteria 1 and 2: asks cvc5 to split a query, then solves every partition in a process of its own. A partition is the
/// query with one more assertion, so a satisfiable partition's values satisfy the query. The query is unsatisfiable only
/// when every partition is and the partitions cover every case, which a second cvc5 process checks from the partitions
/// alone: the declarations and the negation of their disjunction must be unsatisfiable.
/// </summary>
internal sealed class PartitionedSolve(string cvc5, int timeoutMs, Slots slots, string work)
{
    public const string Sat = "sat";
    public const string Unsat = "unsat";
    public const string Undecided = "undecided";
    public const string NoPartitions = "no-partitions";
    public const string Error = "error";

    public static readonly string[] Strategies = ["decision-scatter", "heap-scatter", "lemma-scatter", "decision-cube", "heap-cube", "lemma-cube"];

    /// <summary>The options P1-025 ran cvc5 with. A partition is solved with these and nothing else.</summary>
    public static readonly string[] Plain = ["--arrays-exp"];

    /// <summary>The partitioning options: the count, the strategy, and the check-count trigger at its first check.</summary>
    public static string[] Partitioning(int count, string strategy, string partitions) =>
    [
        .. Plain,
        $"--compute-partitions={count}",
        $"--partition-strategy={strategy}",
        "--partition-when=climit",
        "--checks-before-partition=1",
        "--checks-between-partitions=1",
        $"--write-partitions-to={partitions}",
    ];

    public Solved Solve(string file, int count, string strategy, string tag)
    {
        string partitionsFile = Path.Combine(work, tag + ".partitions");
        File.Delete(partitionsFile);
        int one = slots.Acquire(1);
        Ran split;
        try
        {
            split = Cvc5.RunAsync(cvc5, [.. Partitioning(count, strategy, partitionsFile), file], timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            slots.Release(one);
        }

        // The cube strategies write each cube twice. A partition is one line.
        string[] partitions = File.Exists(partitionsFile)
            ? [.. File.ReadLines(partitionsFile).Select(static l => l.Trim()).Where(static l => l.Length > 0).Distinct(StringComparer.Ordinal)]
            : [];
        File.Delete(partitionsFile);
        if (partitions.Length == 0)
        {
            // Nothing was split. cvc5's own answer stands only when it is satisfiable, since that one is checked by the
            // read-back; after writing partitions cvc5 prints unsat whatever the query is, so its unsat is never taken.
            return split.Status == Sat
                ? new Solved(Sat, 0, "n/a", split.Status, string.Empty, split.WallMs, split.CpuMs, split.Output)
                : new Solved(split.Status == Error ? Error : NoPartitions, 0, "n/a", split.Status, split.WallMs > timeoutMs + 5000 ? "late=1" : split.Error, split.WallMs, split.CpuMs, string.Empty);
        }

        string script = File.ReadAllText(file);
        int taken = slots.Acquire(partitions.Length);
        try
        {
            string coverFile = Path.Combine(work, tag + ".cover.smt2");
            File.WriteAllText(coverFile, Smt.Cover(script, partitions));
            Ran cover = Cvc5.RunAsync(cvc5, [.. Plain, coverFile], timeoutMs, CancellationToken.None).GetAwaiter().GetResult();
            File.Delete(coverFile);

            string[] files = new string[partitions.Length];
            for (int i = 0; i < partitions.Length; i++)
            {
                files[i] = Path.Combine(work, $"{tag}.p{i}.smt2");
                File.WriteAllText(files[i], Smt.With(script, partitions[i]));
            }

            script = string.Empty;
            Stopwatch clock = Stopwatch.StartNew();
            using CancellationTokenSource found = new();
            Ran[] answers = Task.WhenAll(files.Select(async f =>
            {
                Ran answer = await Cvc5.RunAsync(cvc5, [.. Plain, f], timeoutMs, found.Token).ConfigureAwait(false);
                if (answer.Status == Sat)
                {
                    // One satisfiable partition settles the query; the others are stopped.
                    await found.CancelAsync().ConfigureAwait(false);
                }

                return answer;
            })).GetAwaiter().GetResult();
            long wall = split.WallMs + cover.WallMs + clock.ElapsedMilliseconds;
            long cpu = split.CpuMs + cover.CpuMs + answers.Sum(static a => a.CpuMs);
            foreach (string f in files)
            {
                File.Delete(f);
            }

            string status = Combine([.. answers.Select(static a => a.Status)], cover.Status == Unsat);
            string parts = string.Join(',', answers.GroupBy(static a => a.Status).OrderBy(static g => g.Key, StringComparer.Ordinal).Select(static g => $"{g.Key}={g.Count()}"));

            // A process that outlived its limit by more than its own start and stop is a fault of the spike, and is counted.
            int late = ((Ran[])[split, cover, .. answers]).Count(a => a.WallMs > timeoutMs + 5000);
            parts += late > 0 ? $",late={late}" : string.Empty;
            string values = status == Sat ? answers.First(static a => a.Status == Sat).Output : string.Empty;
            return new Solved(status, partitions.Length, cover.Status, split.Status, parts, wall, cpu, values);
        }
        finally
        {
            slots.Release(taken);
        }
    }

    /// <summary>Satisfiable when one partition is; unsatisfiable when every partition is and they cover every case.</summary>
    public static string Combine(IReadOnlyList<string> partitions, bool covers)
    {
        if (partitions.Contains(Sat))
        {
            return Sat;
        }

        if (partitions.All(static p => p == Unsat))
        {
            return covers ? Unsat : Undecided;
        }

        return partitions.Contains(Error) ? Error : Undecided;
    }
}

/// <summary>The two files made from an exported query: a partition, and the check that the partitions cover every case.</summary>
internal static class Smt
{
    private const string CheckSat = "(check-sat)";

    /// <summary>The script with <paramref name="partition"/> asserted before its <c>check-sat</c>.</summary>
    public static string With(string script, string partition)
    {
        int at = script.LastIndexOf(CheckSat, StringComparison.Ordinal);
        return new StringBuilder(script.Length + partition.Length + 16).Append(script, 0, at).Append("(assert ").Append(partition).Append(")\n").Append(script, at, script.Length - at).ToString();
    }

    /// <summary>The script's logic and declarations, and the assertion that no partition holds.</summary>
    public static string Cover(string script, IReadOnlyList<string> partitions)
    {
        StringBuilder cover = new();
        foreach ((int start, int end) in TopLevel(script))
        {
            ReadOnlySpan<char> form = script.AsSpan(start, end - start);
            if (form.StartsWith("(set-logic") || form.StartsWith("(declare-") || form.StartsWith("(define-"))
            {
                cover.Append(form).Append('\n');
            }
        }

        cover.Append("(assert (not (or false");
        foreach (string partition in partitions)
        {
            cover.Append(' ').Append(partition);
        }

        return cover.Append(")))\n").Append(CheckSat).Append('\n').ToString();
    }

    /// <summary>The script's top-level forms. A quoted symbol or a string may hold parentheses.</summary>
    private static IEnumerable<(int Start, int End)> TopLevel(string script)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            if (c is '|' or '"')
            {
                i = script.IndexOf(c, i + 1);
            }
            else if (c == '(')
            {
                if (depth++ == 0)
                {
                    start = i;
                }
            }
            else if (c == ')' && --depth == 0)
            {
                yield return (start, i + 1);
            }
        }
    }
}
