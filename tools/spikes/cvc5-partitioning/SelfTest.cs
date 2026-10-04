using System.Globalization;
using System.Text;

namespace Cvc5PartitioningSpike;

/// <summary>
/// On one sample query that is satisfiable and one that is not, the partitioned answer equals the plain one, at 8
/// partitions with each strategy. The samples put pigeons in holes, as equalities over an uninterpreted sort: cvc5 writes
/// partitions at a theory check, and a query with no theory atom, or one its bit-vector solver settles alone, has none.
/// </summary>
internal static class SelfTest
{
    private const int TimeoutMs = 60_000;

    public static int Run(string cvc5)
    {
        string work = Path.Combine(Path.GetTempPath(), "cvc5-partitioning-self-test-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        bool ok = Combines();
        try
        {
            ok &= Check(cvc5, work, "8 pigeons in 8 holes", 8, PartitionedSolve.Sat);
            ok &= Check(cvc5, work, "9 pigeons in 8 holes", 9, PartitionedSolve.Unsat);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        Console.WriteLine(ok ? "self-test passed" : "self-test FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(string cvc5, string work, string name, int pigeons, string expected)
    {
        const int Holes = 8;
        static string Pigeon(int pigeon) => $"|pigeon ({pigeon}|";
        StringBuilder script = new("(set-option :produce-models true)\n(set-logic QF_UF)\n(declare-sort Hole 0)\n");
        for (int h = 0; h < Holes; h++)
        {
            script.Append(CultureInfo.InvariantCulture, $"(declare-fun hole{h} () Hole)\n");
        }

        // Every pigeon is in a hole, and no hole holds two.
        for (int p = 0; p < pigeons; p++)
        {
            script.Append(CultureInfo.InvariantCulture, $"(declare-fun {Pigeon(p)} () Hole)\n")
                .Append("(assert (or").AppendJoin(string.Empty, Enumerable.Range(0, Holes).Select(h => $" (= {Pigeon(p)} hole{h})")).Append("))\n");
            for (int q = 0; q < p; q++)
            {
                script.Append(CultureInfo.InvariantCulture, $"(assert (not (= {Pigeon(p)} {Pigeon(q)})))\n");
            }
        }

        script.Append(CultureInfo.InvariantCulture, $"(check-sat)\n(get-value ({Pigeon(0)}))\n");
        string file = Path.Combine(work, pigeons.ToString(CultureInfo.InvariantCulture) + ".smt2");
        File.WriteAllText(file, script.ToString());
        string plain = Cvc5.RunAsync(cvc5, [.. PartitionedSolve.Plain, file], TimeoutMs, CancellationToken.None).GetAwaiter().GetResult().Status;
        bool ok = plain == expected;
        PartitionedSolve solver = new(cvc5, TimeoutMs, new Slots(8), work);
        foreach (string strategy in PartitionedSolve.Strategies)
        {
            Solved solved = solver.Solve(file, 8, strategy, strategy);
            // A strategy that writes no partition before cvc5 answers has nothing to compare; the decision strategies must split.
            bool split = solved.Partitions > 1;
            bool same = split
                ? solved.Status == plain && solved.Cover == PartitionedSolve.Unsat && (solved.Status != PartitionedSolve.Sat || solved.Values.Contains("((|pigeon (0| ", StringComparison.Ordinal))
                : !strategy.StartsWith("decision-", StringComparison.Ordinal) && solved.Own == plain;
            ok &= same;
            Console.WriteLine($"{(same ? "ok  " : "FAIL")} {name}, {strategy}: plain {plain}, {(split ? $"partitioned {solved.Status}, {solved.Partitions} partitions ({solved.Parts}), cover check {solved.Cover}" : "not split")}");
        }

        return ok;
    }

    /// <summary>How partition answers make a query's answer.</summary>
    private static bool Combines()
    {
        bool ok = PartitionedSolve.Combine(["unsat", "sat", "stopped"], covers: false) == PartitionedSolve.Sat
            && PartitionedSolve.Combine(["unsat", "unsat"], covers: true) == PartitionedSolve.Unsat
            && PartitionedSolve.Combine(["unsat", "unsat"], covers: false) == PartitionedSolve.Undecided
            && PartitionedSolve.Combine(["unsat", "timeout"], covers: true) == PartitionedSolve.Undecided
            && PartitionedSolve.Combine(["unsat", "unknown"], covers: true) == PartitionedSolve.Undecided
            && PartitionedSolve.Combine(["unsat", "error"], covers: true) == PartitionedSolve.Error;
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} combining partition answers");
        return ok;
    }
}
