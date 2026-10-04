using System.Diagnostics;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace DivergentCount;

/// <summary>
/// Criterion 2: the hashing scheme of approximate model counting (ApproxMC2: Chakraborty, Meel and Vardi, IJCAI 2016), on
/// Z3, projected on the source parameters' bits. The formula is rung 1's divergence query (<see cref="LoopLadder"/>): the
/// product's assertions, some observable differs, neither side reaches an opaque node or the unrolling bound. Every other
/// symbol (heap maps, call functions, the receiver) is existential, so the count is of parameter assignments for which
/// some assignment of the rest diverges.
/// <para>
/// With at most <see cref="Threshold"/> - 1 such assignments the count is exact, by enumeration. Otherwise each iteration
/// draws random parity constraints over the bits, searches for the number <c>m</c> of them that leaves a cell of fewer
/// than <see cref="Threshold"/> assignments, and estimates <c>cell * 2^m</c>; the answer is the median. The whole count
/// has the pair's <see cref="VerificationOptions.TimeoutMs"/>, and each check the pair's resource limit.
/// </para>
/// </summary>
internal sealed class Counter(VerificationOptions options, int seed)
{
    public const double Tolerance = 0.8;

    public const double Confidence = 0.8;

    /// <summary>
    /// The probability one iteration's estimate is within the tolerance, as the ApproxMC2 paper's analysis gives it. From
    /// memory: the paper could not be read from this box. <see cref="Iterations"/> follows from it exactly.
    /// </summary>
    public const double IterationSuccess = 0.6;

    /// <summary>
    /// <c>1 + 9.84 (1 + e/(1+e)) (1 + 1/e)^2</c>, as the reference implementation computes it (meelgroup/approxmc,
    /// <c>counter.cpp</c>): 72 at tolerance 0.8.
    /// </summary>
    public static int Threshold { get; } = (int)(1 + (9.84 * (1 + (Tolerance / (1 + Tolerance))) * (1 + (1 / Tolerance)) * (1 + (1 / Tolerance))));

    /// <summary>
    /// The least odd number of independent iterations whose median is within the tolerance with probability
    /// <see cref="Confidence"/>: the median is wrong only if at least half are, and each is wrong with probability at most
    /// 1 - <see cref="IterationSuccess"/>.
    /// </summary>
    public static int Iterations { get; } = Enumerable.Range(0, 200).Select(static k => (2 * k) + 1).First(static t => MajorityRight(t) >= Confidence);

    private static double MajorityRight(int t)
    {
        double total = 0;
        for (int right = (t / 2) + 1; right <= t; right++)
        {
            double ways = 1;
            for (int i = 0; i < right; i++)
            {
                ways = ways * (t - i) / (i + 1);
            }

            total += ways * Math.Pow(IterationSuccess, right) * Math.Pow(1 - IterationSuccess, t - right);
        }

        return total;
    }

    public Count Run(IrProcedure old, IrProcedure @new, Classification classification)
    {
        (old, @new, _) = ProductEncoder.ShareFragments(old, @new);
        IrProcedure oldUnrolled = IrUnroller.Unroll(old, options.Bound);
        IrProcedure newUnrolled = IrUnroller.Unroll(@new, options.Bound);
        Stopwatch clock = Stopwatch.StartNew();
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, oldUnrolled, newUnrolled, options.CallIdentityMap);
        BoolExpr[] formula = Z3Backend.Inline(
            context,
            encoding.Assertions,
            [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)]);
        Session session = new(context, encoding, formula, Inputs.Bits(context, encoding, classification), options, clock, new Random(seed));
        try
        {
            return session.Count();
        }
        catch (OutOfBudgetException e)
        {
            return new Count(e.Message, classification.Bits, double.NaN, Exact: false, session.Checks, clock.ElapsedMilliseconds);
        }
    }

    /// <summary>The outcome names the report tabulates.</summary>
    public const string Counted = "counted";

    public const string NotDivergent = "not-divergent-at-this-commit";

    public const string Timeout = "timeout";

    public const string GaveUp = "solver-gave-up";

    private sealed class OutOfBudgetException(string outcome) : Exception(outcome);

    private sealed class Session(Context context, ProductEncoding encoding, BoolExpr[] formula, BoolExpr[] bits, VerificationOptions options, Stopwatch clock, Random random)
    {
        public int Checks { get; private set; }

        public Count Count()
        {
            int n = bits.Length;
            int all = Cell(([], bits));
            if (all == 0)
            {
                return new Count(NotDivergent, n, double.NegativeInfinity, Exact: true, Checks, clock.ElapsedMilliseconds);
            }

            if (all < Threshold)
            {
                return new Count(Counted, n, Math.Log2(all) - n, Exact: true, Checks, clock.ElapsedMilliseconds);
            }

            List<double> estimates = [];
            int previous = 1;
            for (int i = 0; i < Iterations; i++)
            {
                (bool[] Mask, bool Parity)[] rows = [.. Enumerable.Range(0, n).Select(_ => Parity())];
                Dictionary<int, int> cells = new() { [0] = Threshold };
                int m = Search(rows, cells, previous);
                if (cells[m] is > 0 and var cell && cell < Threshold)
                {
                    estimates.Add(Math.Min(Math.Log2(cell) + m, n));
                    previous = m;
                }
            }

            if (estimates.Count == 0)
            {
                throw new OutOfBudgetException(GaveUp);
            }

            estimates.Sort();
            return new Count(Counted, n, estimates[estimates.Count / 2] - n, Exact: false, Checks, clock.ElapsedMilliseconds);
        }

        /// <summary>
        /// The <c>m</c> with fewer than <see cref="Threshold"/> assignments under the first <c>m</c> rows and at least that
        /// many under the first <c>m - 1</c> (the paper's LogSATSearch): from the last iteration's <c>m</c>, one step at a
        /// time while near it, then doubling, then bisecting.
        /// </summary>
        private int Search((bool[] Mask, bool Parity)[] rows, Dictionary<int, int> cells, int previous)
        {
            int n = rows.Length;
            int low = 0;
            int high = n + 1;
            int m = Math.Clamp(previous, 1, n);
            while (high - low > 1)
            {
                bool near = Math.Abs(m - previous) < 3;
                if (Probe(m) >= Threshold)
                {
                    low = m;
                    m = near ? m + 1 : 2 * m < high ? 2 * m : (m + high) / 2;
                }
                else
                {
                    high = m;
                    m = near ? m - 1 : (low + m) / 2;
                }

                m = Math.Clamp(m, low + 1, Math.Max(high - 1, low + 1));
            }

            // Every prefix up to n left a full cell: the iteration has no estimate, which the caller sees in cells[n].
            return Math.Min(high, n);

            int Probe(int size)
            {
                if (!cells.TryGetValue(size, out int cell))
                {
                    cell = Cell(Reduce(rows[..size]));
                    cells[size] = cell;
                }

                return cell;
            }
        }

        /// <summary>A random parity constraint: each bit with probability one half, and a random parity.</summary>
        private (bool[] Mask, bool Parity) Parity() => ([.. bits.Select(_ => random.Next(2) == 0)], random.Next(2) == 0);

        /// <summary>
        /// The rows as terms, after Gaussian elimination over GF(2): each surviving row defines one pivot bit as the parity
        /// of bits that are no row's pivot, so a cell of <c>m</c> independent rows over <c>n</c> bits has rows of at most
        /// <c>n - m + 1</c> bits. The constraint is the same; the solver gives up on the unreduced rows. A model is blocked on the bits that are no
        /// row's pivot (the second result), which fix the others. A row that
        /// reduces to <c>0 = 1</c> makes the cell empty.
        /// </summary>
        private (BoolExpr[] Rows, BoolExpr[] Free) Reduce((bool[] Mask, bool Parity)[] rows)
        {
            List<(bool[] Mask, bool Parity, int Pivot)> reduced = [];
            foreach ((bool[] mask, bool parity) in rows)
            {
                bool[] row = [.. mask];
                bool p = parity;
                foreach ((bool[] other, bool otherParity, int pivot) in reduced)
                {
                    if (row[pivot])
                    {
                        Xor(row, other);
                        p ^= otherParity;
                    }
                }

                int lead = Array.IndexOf(row, true);
                if (lead < 0)
                {
                    if (p)
                    {
                        return ([context.MkFalse()], bits);
                    }

                    continue;
                }

                for (int i = 0; i < reduced.Count; i++)
                {
                    if (reduced[i].Mask[lead])
                    {
                        Xor(reduced[i].Mask, row);
                        reduced[i] = (reduced[i].Mask, reduced[i].Parity ^ p, reduced[i].Pivot);
                    }
                }

                reduced.Add((row, p, lead));
            }

            HashSet<int> pivots = [.. reduced.Select(static r => r.Pivot)];
            return (
                [.. reduced.Select(r => context.MkEq(
                    bits[r.Pivot],
                    Enumerable.Range(0, bits.Length).Where(i => i != r.Pivot && r.Mask[i]).Aggregate(context.MkBool(r.Parity), (parity, i) => context.MkXor(parity, bits[i]))))],
                [.. bits.Where((_, i) => !pivots.Contains(i))]);

            static void Xor(bool[] into, bool[] from)
            {
                for (int i = 0; i < into.Length; i++)
                {
                    into[i] ^= from[i];
                }
            }
        }

        /// <summary>
        /// How many assignments of the bits satisfy the formula under <paramref name="rows"/>, up to
        /// <see cref="Threshold"/>: each model's assignment is blocked and the solver asked again. The solver is Z3's
        /// incremental one first, and <see cref="Z3Backend.Query"/>'s tactic pipeline when that gives up. The pipeline
        /// starts over at every check: on the self-test it is two to five times slower and runs one pair out of its
        /// budget. The formula is inlined as that query inlines it.
        /// </summary>
        private int Cell((BoolExpr[] Rows, BoolExpr[] Free) cell)
        {
            using Solver incremental = context.MkSolver();
            if (Enumerate(incremental, cell) is { } found)
            {
                return found;
            }

            // The incremental solver skips preprocessing and gives up on some products the verdict's own query decides.
            using Tactic solveEqs = context.MkTactic("solve-eqs");
            using Tactic simplify = context.MkTactic("simplify");
            using Tactic propagate = context.MkTactic("propagate-values");
            using Tactic smt = context.MkTactic("smt");
            using Tactic pipeline = context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
            using Solver preprocessed = context.MkSolver(pipeline);
            return Enumerate(preprocessed, cell) ?? throw new OutOfBudgetException(GaveUp);
        }

        /// <summary>The cell's size up to <see cref="Threshold"/>, or null when a check gave up inside the budget.</summary>
        private int? Enumerate(Solver solver, (BoolExpr[] Rows, BoolExpr[] Free) cell)
        {
            solver.Set(Z3Backend.ResourceLimitParameter, (uint)options.ResourceLimit);
            solver.Add(encoding.Assertions);
            solver.Add(formula);
            solver.Add(cell.Rows);
            int found = 0;
            while (found < Threshold)
            {
                long left = options.TimeoutMs - clock.ElapsedMilliseconds;
                if (left <= 0)
                {
                    throw new OutOfBudgetException(Timeout);
                }

                solver.Set(Z3Backend.TimeoutParameter, (uint)left);
                Checks++;
                Status status = Z3Backend.Interruptible(context, left + 1000, () => solver.Check());
                if (status == Status.UNSATISFIABLE)
                {
                    break;
                }

                if (status == Status.UNKNOWN)
                {
                    if (clock.ElapsedMilliseconds >= options.TimeoutMs)
                    {
                        throw new OutOfBudgetException(Timeout);
                    }

                    Console.Error.WriteLine($"  check gave up under {cell.Rows.Length} rows after {found} models: {solver.ReasonUnknown}");
                    return null;
                }

                found++;
                if (cell.Free.Length == 0)
                {
                    break;
                }

                using Model model = solver.Model;
                solver.Add(context.MkOr(cell.Free.Select(b => model.Eval(b, completion: true).IsTrue ? context.MkNot(b) : b)));
            }

            return found;
        }
    }
}

/// <summary>
/// One pair's count: <see cref="Log2Share"/> is the base-2 logarithm of the share of the parameter space that diverges
/// (0 is every assignment, <c>-Bits</c> a single one), exact or within the tolerance.
/// </summary>
internal sealed record Count(string Outcome, int Bits, double Log2Share, bool Exact, int Checks, long Milliseconds);
