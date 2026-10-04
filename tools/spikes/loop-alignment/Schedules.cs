using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace LoopAlignmentSpike;

/// <summary>
/// "After <see cref="A"/> legacy and <see cref="B"/> modern iterations of loop <see cref="Loop"/>, every <see cref="M"/>
/// legacy iterations pair with <see cref="N"/> modern ones." Every other loop pairs visit for visit.
/// </summary>
internal readonly record struct Schedule(int Loop, int A, int B, int M, int N)
{
    public bool Lockstep => A == 0 && B == 0 && M == 1 && N == 1;

    /// <summary>The schedule class the report groups by; the loop it applies to is left out.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{M}:{N}") + (A + B == 0 ? ", no offset" : string.Create(CultureInfo.InvariantCulture, $", offset {A} legacy and {B} modern"));
}

/// <summary>What the runs of one pair show: the first schedule that fits them all, or why none does.</summary>
internal sealed record Finding(Schedule? Schedule, string? Cause, int Usable, int Reaching, int MostVisits, bool OutcomesAgree);

internal static class Schedules
{
    /// <summary>The inputs both sides run on, as <c>TraceInvariantProposer.MaxInputs</c>.</summary>
    public const int Inputs = 200;

    private const ulong Seed = 0x9E3779B97F4A7C15;

    /// <summary>
    /// Runs both sides on <see cref="Inputs"/> inputs and returns the first schedule, in order of <c>m + n</c> and then of
    /// <c>a + b</c>, that fits every usable run and that some run exercises: the legacy side visits the loop's header at
    /// least <c>a + m + 1</c> times and the modern side at least <c>b + n + 1</c> times, one whole paired stretch.
    /// The two sides must have the same loop forest.
    /// </summary>
    public static Finding Search(IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape)
    {
        Side oldSide = new(old, oldShape);
        Side newSide = new(@new, newShape);
        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(old, @new);
        List<(Trace Old, Trace New)> runs = [];
        bool opaque = false;
        for (int r = 0; r < Inputs; r++)
        {
            Rng rng = new(Seed + (ulong)r, trueInFour: 2);
            Dictionary<SharedParameter, IrValue> inputs = shared.ToDictionary(static s => s, s => rng.Value(s.Type));
            Trace oldRun = oldSide.Run([.. old.Parameters.Select(p => inputs[shared.First(s => s.Old == p)])], new Oracle((ulong)r, 1 + (r % 3)));
            Trace newRun = newSide.Run([.. @new.Parameters.Select(p => inputs[shared.First(s => s.New == p)])], new Oracle((ulong)r, 1 + (r % 3)));
            opaque |= oldRun.End == "opaque" || newRun.End == "opaque";
            if (oldRun.End is null && newRun.End is null)
            {
                runs.Add((oldRun, newRun));
            }
        }

        int reaching = runs.Count(static r => r.Old.Visits.Count + r.New.Visits.Count > 0);
        int most = runs.Select(static r => Math.Max(r.Old.Visits.Count, r.New.Visits.Count)).DefaultIfEmpty(0).Max();
        bool agree = runs.TrueForAll(static r => r.Old.Run.Outcome == r.New.Run.Outcome && r.Old.Run.Outs.SequenceEqual(r.New.Run.Outs));
        Schedule? First(IEqualityComparer<IrCallRecord> events) => reaching == 0 ? null : All(oldSide.Parents.Length)
            .Where(s => runs.Exists(r => r.Old.Visits.Count(v => v == s.Loop) > s.A + s.M && r.New.Visits.Count(v => v == s.Loop) > s.B + s.N))
            .Cast<Schedule?>()
            .FirstOrDefault(s => runs.TrueForAll(r => Fits(s!.Value, r.Old, r.New, oldSide.Parents, events)));
        Schedule? fit = First(EqualityComparer<IrCallRecord>.Default);

        // No difference and no fit: the two sides agree visit for visit, but no usable run goes round a loop once.
        string? difference = fit is null && reaching > 0 ? Difference(runs, oldSide.Parents) : null;
        string? cause = (fit, reaching == 0 || difference is null, opaque) switch
        {
            (not null, _, _) => null,
            (_, true, true) => "an opaque in the loop or before it",
            (_, true, false) => reaching == 0 ? "no run that reaches the loop" : "no run that goes round the loop",
            _ => $"other: no schedule; first difference in lockstep: {difference}; by callee alone: {First(CalleeOnly.Instance)?.ToString() ?? "none"}",
        };
        return new Finding(fit, cause, runs.Count, reaching, most, agree);
    }

    /// <summary>What first differs between the two sides paired visit for visit, on the most runs that differ.</summary>
    private static string? Difference(List<(Trace Old, Trace New)> runs, ImmutableArray<int> parents) =>
        runs.Select(r => Difference(Reduce(r.Old, parents, 0, 0, 1), Reduce(r.New, parents, 0, 0, 1)))
            .OfType<string>()
            .GroupBy(static d => d, StringComparer.Ordinal)
            .OrderByDescending(static g => g.Count()).ThenBy(static g => g.Key, StringComparer.Ordinal)
            .Select(static g => g.Key)
            .FirstOrDefault();

    private static string? Difference((List<int> Points, List<List<IrCallRecord>> Stretches) old, (List<int> Points, List<List<IrCallRecord>> Stretches) @new)
    {
        for (int s = 0; s < Math.Min(old.Stretches.Count, @new.Stretches.Count); s++)
        {
            (List<IrCallRecord> a, List<IrCallRecord> b) = (old.Stretches[s], @new.Stretches[s]);
            int at = a.Zip(b).TakeWhile(static p => p.First.Equals(p.Second)).Count();
            if (at < Math.Min(a.Count, b.Count))
            {
                return a[at].Callee != b[at].Callee ? "the callee" : !a[at].Arguments.SequenceEqual(b[at].Arguments) ? "a call's arguments" : "the heap a call reads";
            }

            if (a.Count != b.Count)
            {
                return "one side calls where the other reaches a header or leaves";
            }

            if (s < Math.Min(old.Points.Count, @new.Points.Count) && old.Points[s] != @new.Points[s])
            {
                return "which loop is reached";
            }
        }

        return old.Points.Count == @new.Points.Count ? null : "the number of header visits";
    }

    /// <summary>Every schedule of criterion 2's bounds, simplest first. Lockstep is the same on every loop, so it is listed once.</summary>
    public static IEnumerable<Schedule> All(int loops) =>
        from m in Enumerable.Range(1, 4)
        from n in Enumerable.Range(1, 4)
        from a in Enumerable.Range(0, 3)
        from b in Enumerable.Range(0, 3)
        from loop in Enumerable.Range(0, loops)
        let schedule = new Schedule(loop, a, b, m, n)
        where loop == 0 || !schedule.Lockstep
        orderby m + n, a + b, loop, m, a
        select schedule;

    /// <summary>
    /// Whether one run fits: both sides reach the same paired points in the same order, with equal call events on every
    /// stretch between two of them, before the first and after the last. A side may leave its loop inside a stretch (an
    /// unrolled body keeps its exit tests), so the last stretch is compared up to the exit.
    /// </summary>
    public static bool Fits(Schedule schedule, Trace old, Trace @new, ImmutableArray<int> parents, IEqualityComparer<IrCallRecord> events)
    {
        (List<int> oldPoints, List<List<IrCallRecord>> oldStretches) = Reduce(old, parents, schedule.Loop, schedule.A, schedule.M);
        (List<int> newPoints, List<List<IrCallRecord>> newStretches) = Reduce(@new, parents, schedule.Loop, schedule.B, schedule.N);
        return oldPoints.SequenceEqual(newPoints) && oldStretches.Zip(newStretches).All(p => p.First.SequenceEqual(p.Second, events));
    }

    /// <summary>
    /// One side's paired points and the call events between them. A visit of the scheduled loop is a paired point when it
    /// is visit <c>offset + k * step</c> since the loop was entered; a visit of any other loop always is.
    /// </summary>
    private static (List<int> Points, List<List<IrCallRecord>> Stretches) Reduce(Trace trace, ImmutableArray<int> parents, int target, int offset, int step)
    {
        int[] ordinal = new int[parents.Length];
        List<int> points = [];
        List<List<IrCallRecord>> stretches = [[.. trace.Stretches[0]]];
        for (int v = 0; v < trace.Visits.Count; v++)
        {
            int loop = trace.Visits[v];
            for (int inner = 0; inner < parents.Length; inner++)
            {
                // Visiting an enclosing header means the inner loop is entered afresh next time.
                ordinal[inner] = Encloses(parents, loop, inner) ? 0 : ordinal[inner];
            }

            int visit = ordinal[loop]++;
            if (loop != target || (visit >= offset && (visit - offset) % step == 0))
            {
                points.Add(loop);
                stretches.Add([]);
            }

            stretches[^1].AddRange(trace.Stretches[v + 1]);
        }

        return (points, stretches);
    }

    private static bool Encloses(ImmutableArray<int> parents, int outer, int inner)
    {
        for (int loop = parents[inner]; loop >= 0; loop = parents[loop])
        {
            if (loop == outer)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Two call events are equal when their callees are: the diagnostic that asks whether arguments alone stop a schedule.</summary>
internal sealed class CalleeOnly : IEqualityComparer<IrCallRecord>
{
    public static readonly CalleeOnly Instance = new();

    public bool Equals(IrCallRecord? x, IrCallRecord? y) => x?.Callee == y?.Callee;

    public int GetHashCode(IrCallRecord record) => Rng.Hash(record.Callee.Value).GetHashCode();
}
