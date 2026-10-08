using System.Diagnostics;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Progress;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;
using Equiv.Verify.Z3.Refinement;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace HardQueriesSpike;

/// <summary>
/// P2-101: what do the queries behind the timeout Unknowns a larger budget does not decide have in common, and does
/// another tactic pipeline answer them? <c>--export</c> loads the pair through the production frontend, finds for each
/// identity the query Z3 gives up on with the production solver and limits, and writes it out with its features.
/// <c>--try</c> reads an exported query back and asks it of a solver built another way. Prints identities, statuses and
/// counts; the output folder holds names and constants from the analysed code, so it belongs under <c>.corpus/</c>.
/// </summary>
internal static class Program
{
    public const string Located = "located.tsv";
    public const string Tried = "tried.tsv";

    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--export", string identities, string legacy, string modern, string outDir, .. string[] rest] when rest.Length % 2 == 0:
                return Export(identities, legacy, modern, outDir, Options(rest));
            case ["--try", string outDir, .. string[] rest] when rest.Length % 2 == 0:
                return Try(outDir, Options(rest));
            case ["--tactics"]:
                using (Context context = new())
                {
                    Console.Out.WriteLine(string.Join('\n', context.TacticNames.Order(StringComparer.Ordinal)));
                }

                return 0;
            default:
                Console.Error.WriteLine(
                    "usage: hard-queries-spike --export <identities.txt> <legacy.sln> <modern.sln> <outDir> [--threads n] [--only i,j] [--verify true|false]"
                    + " | --try <outDir> --variants <name=tactic,tactic/inline|raw;...> [--only i,j] [--threads n] [--rlimit n] [--timeout ms] [--file suffix]"
                    + " | --tactics");
                return 2;
        }
    }

    private static Dictionary<string, string> Options(string[] rest)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        for (int i = 0; i < rest.Length; i += 2)
        {
            options[rest[i]] = rest[i + 1];
        }

        return options;
    }

    private static HashSet<int>? Only(Dictionary<string, string> options) =>
        options.TryGetValue("--only", out string? only) ? [.. only.Split(',').Select(static n => int.Parse(n, CultureInfo.InvariantCulture))] : null;

    private static int Threads(Dictionary<string, string> options) =>
        options.TryGetValue("--threads", out string? threads) ? int.Parse(threads, CultureInfo.InvariantCulture) : 1;

    private static int Export(string identitiesPath, string legacy, string modern, string outDir, Dictionary<string, string> flags)
    {
        string[] identities = [.. File.ReadAllLines(identitiesPath).Where(static l => l.Length > 0)];
        HashSet<int>? only = Only(flags);
        bool verify = !flags.TryGetValue("--verify", out string? v) || bool.Parse(v);
        Directory.CreateDirectory(outDir);
        string located = Path.Combine(outDir, Located);
        HashSet<int> done = File.Exists(located) ? [.. File.ReadLines(located).Select(static l => int.Parse(l.Split('\t')[0], CultureInfo.InvariantCulture))] : [];

        Stopwatch clock = Stopwatch.StartNew();
        FrontendAnalysis analysis = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, NullRunLog.Instance, CancellationToken.None);
        Dictionary<string, ProcedurePair> pairs = new(StringComparer.Ordinal);
        foreach (ProcedurePair pair in analysis.Match.Pairs)
        {
            pairs[pair.New.Value] = pair;
        }

        Console.Error.WriteLine($"loaded and lowered {analysis.Match.Pairs.Length} pairs in {clock.Elapsed.TotalSeconds:F0} s");
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, config.TimeoutMs, config.CallIdentityRenames)
        {
            ResourceLimit = flags.TryGetValue("--rlimit", out string? r) ? int.Parse(r, CultureInfo.InvariantCulture) : config.ResourceLimit,
        };
        object gate = new();
        int finished = 0;
        (string Identity, int Index)[] items = [.. identities.Select(static (identity, index) => (identity, index)).Where(item => only?.Contains(item.index) != false && !done.Contains(item.index))];
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Threads(flags) }, item =>
        {
            Stopwatch one = Stopwatch.StartNew();
            string line;
            try
            {
                line = pairs.GetValueOrDefault(item.Identity) is { OldBody: { } old, NewBody: { } @new }
                    ? new Locator(item.Index, old, @new, options, outDir).Run(verify)
                    : "pair not found at this commit";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                line = "crash: " + exception.GetType().Name;
            }

            lock (gate)
            {
                File.AppendAllText(located, $"{item.Index}\t{item.Identity}\t{one.ElapsedMilliseconds}\t{line}\n");
                Console.Error.WriteLine($"[{++finished}/{items.Length}] {item.Index:D3} {one.Elapsed.TotalSeconds:F0} s {line.Split('\t')[0]}");
            }
        });
        return 0;
    }

    private static int Try(string outDir, Dictionary<string, string> flags)
    {
        HashSet<int>? only = Only(flags);
        string suffix = flags.GetValueOrDefault("--file", ".r1");
        Variant[] variants = [.. flags["--variants"].Split(';').Select(Variant.Parse)];
        EquivConfig config = EquivConfig.Default;
        VerificationOptions options = new(config.Bound, flags.TryGetValue("--timeout", out string? t) ? int.Parse(t, CultureInfo.InvariantCulture) : config.TimeoutMs, config.CallIdentityRenames)
        {
            ResourceLimit = flags.TryGetValue("--rlimit", out string? r) ? int.Parse(r, CultureInfo.InvariantCulture) : config.ResourceLimit,
        };
        string tried = Path.Combine(outDir, Tried);
        HashSet<string> done = File.Exists(tried) ? [.. File.ReadLines(tried).Select(static l => l.Split('\t')).Select(static c => $"{c[0]}/{c[1]}/{c[2]}/{c[3]}")] : [];
        List<(int Index, string File, Variant Variant)> items = [];
        foreach (string file in Directory.EnumerateFiles(outDir, "???" + suffix + ".smt2").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (name.Length != 3 + suffix.Length + 5)
            {
                continue;
            }

            int index = int.Parse(name[..3], CultureInfo.InvariantCulture);
            items.AddRange(variants
                .Where(variant => only?.Contains(index) != false && !done.Contains($"{index}/{suffix}/{variant.Name}/{options.ResourceLimit}"))
                .Select(variant => (index, file, variant)));
        }

        Console.Error.WriteLine($"{items.Count} checks to run, resource limit {options.ResourceLimit}, timeout {options.TimeoutMs} ms");
        object gate = new();
        int finished = 0;
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = Threads(flags) }, item =>
        {
            Answer answer = Ask(item.File, item.Variant, options);
            lock (gate)
            {
                File.AppendAllText(tried, $"{item.Index}\t{suffix}\t{item.Variant.Name}\t{options.ResourceLimit}\t{answer.Status}\t{answer.Reason}\t{answer.Milliseconds}\t{answer.Spent}\t{answer.Terms}\n");
                Console.Error.WriteLine($"[{++finished}/{items.Count}] {item.Index:D3} {item.Variant.Name} {answer.Status} {answer.Reason} {answer.Milliseconds} ms");
            }
        });
        return 0;
    }

    /// <summary>
    /// Asks one exported query of one solver. The assertions and the query's terms are read back in a context of their
    /// own, the query is inlined or not as the variant says, and the check runs as a production check does
    /// (<see cref="SolverQuery"/>), under the same two limits.
    /// </summary>
    private static Answer Ask(string file, Variant variant, VerificationOptions options)
    {
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            string text = File.ReadAllText(file);
            (int assertionCount, int queryCount) = Smt.Counts(text);
            using Context home = new();
            BoolExpr[] all = home.ParseSMTLIB2String(text);
            if (all.Length != assertionCount + queryCount)
            {
                return new Answer("error", $"read {all.Length} assertions, wrote {assertionCount + queryCount}", clock.ElapsedMilliseconds, 0, 0);
            }

            BoolExpr[] assertions = all[..assertionCount];
            BoolExpr[] query = variant.Inline ? Z3Backend.Inline(home, assertions, all[assertionCount..]) : all[assertionCount..];
            int terms = Features.Size([.. assertions, .. query]);
            using SolverQuery asked = new(home, solving => variant.Solver(solving, options), assertions);
            asked.Add(query);
            clock.Restart();
            Status status = asked.Check(options, variant.Name);
            long ms = clock.ElapsedMilliseconds;
            return new Answer(
                status switch { Status.SATISFIABLE => "sat", Status.UNSATISFIABLE => "unsat", _ => "unknown" },
                status == Status.UNKNOWN ? asked.ReasonUnknown.ReplaceLineEndings(" ") : string.Empty,
                ms,
                Spent(asked.Solver),
                terms);
        }
        catch (Z3Exception exception)
        {
            string message = exception.Message.ReplaceLineEndings(" ");
            return new Answer("error", message[..Math.Min(message.Length, 120)], clock.ElapsedMilliseconds, 0, 0);
        }
    }

    private static uint Spent(Solver solver)
    {
        try
        {
            using Statistics statistics = solver.Statistics;
            return statistics.Entries.FirstOrDefault(static e => e.Key == "rlimit count")?.UIntValue ?? 0;
        }
        catch (Z3Exception)
        {
            return 0;
        }
    }
}

internal sealed record Answer(string Status, string Reason, long Milliseconds, uint Spent, int Terms);

/// <summary>
/// A way to ask a query: the tactics its solver is made of, in order, or <c>default</c> for Z3's own solver, and
/// whether the query's terms get the definitions substituted in (<see cref="Z3Backend.Inline"/>) as production's do.
/// A tactic may carry parameters: <c>smt[relevancy=0:random_seed=3]</c>.
/// </summary>
internal sealed record Variant(string Name, string[] Tactics, bool Inline)
{
    public const string Default = "default";

    /// <summary><c>name=tactic,tactic/inline</c> or <c>/raw</c>.</summary>
    public static Variant Parse(string text)
    {
        string[] named = text.Split('=', 2);
        int form = named[1].LastIndexOf('/');
        return new Variant(named[0], named[1][..form].Split(','), named[1][(form + 1)..] == "inline");
    }

    public Solver Solver(Context context, VerificationOptions options)
    {
        Solver solver;
        if (Tactics is [Default])
        {
            solver = context.MkSolver();
        }
        else
        {
            Tactic[] tactics = [.. Tactics.Select(t => Tactic(context, t))];
            using Tactic all = tactics.Length == 1 ? tactics[0] : context.AndThen(tactics[0], tactics[1], tactics[2..]);
            solver = context.MkSolver(all);
            foreach (Tactic tactic in tactics)
            {
                tactic.Dispose();
            }
        }

        Z3Backend.Limit(solver, options);
        return solver;
    }

    private static Tactic Tactic(Context context, string text)
    {
        int open = text.IndexOf('[', StringComparison.Ordinal);
        if (open < 0)
        {
            return context.MkTactic(text);
        }

        using Tactic plain = context.MkTactic(text[..open]);
        using Params parameters = context.MkParams();
        foreach (string[] pair in text[(open + 1)..^1].Split(':').Select(static p => p.Split('=', 2)))
        {
            if (bool.TryParse(pair[1], out bool flag))
            {
                parameters.Add(pair[0], flag);
            }
            else if (uint.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
            {
                parameters.Add(pair[0], number);
            }
            else
            {
                parameters.Add(pair[0], context.MkSymbol(pair[1]));
            }
        }

        return context.UsingParams(plain, parameters);
    }
}

/// <summary>An exported query: the encoding's assertions, then the query's terms, as a plain solver prints them.</summary>
internal static class Smt
{
    private const string Prefix = "; p2-101 assertions=";

    public static void Write(string path, Context context, IReadOnlyList<BoolExpr> assertions, BoolExpr[] query)
    {
        using Solver solver = context.MkSolver();
        solver.Add(assertions);
        solver.Add(query);
        if (solver.NumAssertions != assertions.Count + query.Length)
        {
            throw new InvalidOperationException("the solver does not hold the assertions one for one");
        }

        File.WriteAllText(path, $"{Prefix}{assertions.Count} query={query.Length}\n{solver}\n");
    }

    public static (int Assertions, int Query) Counts(string text)
    {
        string[] header = text[Prefix.Length..text.IndexOf('\n', StringComparison.Ordinal)].Split(" query=");
        return (int.Parse(header[0], CultureInfo.InvariantCulture), int.Parse(header[1], CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Finds the query that stands between one pair and a verdict, asking what the ladder asks in the ladder's order with
/// the production solver and limits: rung 1's queries on the unrolled pair (<c>LoopLadder.Bounded</c>), then, for a
/// looping pair rung 1 did not decide, rung 2's base and step obligations (<c>LockstepInduction.Prove</c>). The first
/// query of each rung that Z3 gives up on is written out with its features.
/// </summary>
internal sealed class Locator(int index, IrProcedure oldBody, IrProcedure newBody, VerificationOptions options, string outDir)
{
    private readonly List<string> steps = [];
    private readonly List<string> hard = [];

    public string Run(bool verify)
    {
        ProductEncoder.SharedFragments shared = ProductEncoder.ShareFragments(oldBody, newBody);
        IrProcedure old = shared.Old;
        IrProcedure @new = shared.New;
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);
        bool looping = oldShape.IsSelfRecursive || newShape.IsSelfRecursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        bool arithmetic = false;
        bool decided = false;
        if (!oldShape.IsReducible || !newShape.IsReducible)
        {
            steps.Add("r1:not-applicable(irreducible)");
        }
        else if ((IrUnroller.InliningObstacle(old) ?? IrUnroller.InliningObstacle(@new)) is not null)
        {
            steps.Add("r1:not-applicable(self-recursion)");
        }
        else if (LoopLadder.Unrolled(old, @new, options.Bound) is not { } unrolled)
        {
            steps.Add("r1:not-applicable(too-large)");
        }
        else
        {
            arithmetic = ArithmeticAbstraction.AppliesTo(unrolled.Old) || ArithmeticAbstraction.AppliesTo(unrolled.New);
            decided = Rung1(unrolled, looping);
        }

        if (looping && !decided)
        {
            Rung2(old, @new, oldShape, newShape);
        }

        string production = string.Empty;
        if (verify)
        {
            Verdict verdict = new Z3Backend().Verify(oldBody, newBody, options with { RefineTimeouts = false });
            production = verdict switch
            {
                Equivalent => "Equivalent",
                Divergent => "Divergent",
                Unknown unknown => "Unknown(" + unknown.Reason + ")",
                _ => verdict.GetType().Name,
            } + " [" + string.Join(",", verdict.Ladder.Select(static s => $"{s.Rung}:{s.Outcome}")) + "]";
        }

        // summary, looping, hard arithmetic, the steps, production's verdict, then one group of columns per hard query.
        string summary = hard.Count > 0 ? "hard" : decided ? "decided" : "undecided";
        return string.Join('\t', [summary, looping ? "loop" : "no-loop", arithmetic ? "hard-arithmetic" : "no-hard-arithmetic", string.Join(' ', steps), production, .. hard]);
    }

    /// <summary>Rung 1's queries in order; true when one of them gives the pair its verdict.</summary>
    private bool Rung1((IrProcedure Old, IrProcedure New) unrolled, bool looping)
    {
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, unrolled.Old, unrolled.New, options.CallIdentityMap, traces: ProductEncoder.TraceComparison.Positional);
        BoolExpr[] reachable = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        (string Name, BoolExpr[] Terms)[] queries =
        [
            ("divergence", [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable]),
            ("opaque", [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable]),
            ("bound", [context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable)]),
        ];
        for (int i = 0; i < (looping ? 3 : 2); i++)
        {
            Status status = Ask(context, encoding, queries[i].Terms, "r1", queries[i].Name);
            if (status == Status.UNKNOWN)
            {
                // The later queries of the rung, for whoever proves this one unsatisfiable.
                for (int later = i + 1; later < (looping ? 3 : 2); later++)
                {
                    Smt.Write(Path.Combine(outDir, $"{index:D3}.r1-{queries[later].Name}.smt2"), context, encoding.Assertions, queries[later].Terms);
                }

                return false;
            }

            if (status == Status.SATISFIABLE)
            {
                // A model of the last query says only that some input goes past the bound.
                return queries[i].Name != "bound";
            }
        }

        return true;
    }

    private void Rung2(IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape)
    {
        LockstepInduction lockstep = new(new LoopLadder(static () => new Context(), options), old, @new, oldShape, newShape);
        if (lockstep.Misalignment is not null)
        {
            steps.Add("r2:not-applicable(misaligned)");
            return;
        }

        LockstepInduction.Coupling[] couplings = [.. lockstep.Loops.Select(l => LockstepInduction.Couple(old, @new, l.Old, l.New))];
        (IrBlockId Header, System.Collections.Immutable.ImmutableArray<IrVar> State)[] oldCuts = [.. lockstep.Loops.Zip(couplings, static (l, c) => (l.Old, c.Old))];
        (IrBlockId Header, System.Collections.Immutable.ImmutableArray<IrVar> State)[] newCuts = [.. lockstep.Loops.Zip(couplings, static (l, c) => (l.New, c.New))];
        for (int i = -1; i < lockstep.Loops.Length; i++)
        {
            IrBlockId? oldStart = i < 0 ? null : lockstep.Loops[i].Old;
            IrBlockId? newStart = i < 0 ? null : lockstep.Loops[i].New;
            using Context context = new();
            ProductEncoding encoding = ProductEncoder.Encode(context, IrFragmenter.Segment(old, oldStart, oldCuts), IrFragmenter.Segment(@new, newStart, newCuts), options.CallIdentityMap);
            BoolExpr[] terms =
            [
                context.MkTrue(),
                context.MkOr(encoding.Differs, context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), context.MkFalse()),
                context.MkNot(encoding.Old.Unreachable),
                context.MkNot(encoding.New.Unreachable),
            ];
            if (Ask(context, encoding, terms, "r2", i < 0 ? "base" : $"step{i + 1}") != Status.UNSATISFIABLE)
            {
                return;
            }
        }
    }

    private Status Ask(Context context, ProductEncoding encoding, BoolExpr[] terms, string rung, string name)
    {
        Stopwatch clock = Stopwatch.StartNew();
        using SolverQuery query = Z3Backend.Query(context, encoding, options, terms);
        Status status = query.Check(options, name);
        string answer = status switch { Status.SATISFIABLE => "sat", Status.UNSATISFIABLE => "unsat", _ => "unknown" };
        steps.Add($"{rung}:{name}={answer}");
        if (status == Status.UNKNOWN)
        {
            Smt.Write(Path.Combine(outDir, $"{index:D3}.{rung}.smt2"), context, encoding.Assertions, terms);
            Features features = Features.Of([.. encoding.Assertions, .. terms]);
            int inlined = Features.Size([.. encoding.Assertions, .. Z3Backend.Inline(context, encoding.Assertions, terms)]);
            hard.Add(string.Join('\t', rung, name, query.ReasonUnknown.ReplaceLineEndings(" "), clock.ElapsedMilliseconds, encoding.Assertions.Length + terms.Length, inlined, features.Line()));
        }

        return status;
    }
}
