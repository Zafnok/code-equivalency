using System.Runtime.ExceptionServices;

using Equiv.Core;

namespace Equiv.Cli;

/// <summary>
/// The threads the <c>verify</c> and <c>contracts</c> phases share their items among (ticket P2-077). Each backend call
/// already has a solver context of its own, so the items are independent, and the caller puts their results back in
/// order. Threads must never end a query sooner than one thread would (<see cref="Sharing"/>).
/// </summary>
internal static class PairWorkers
{
    /// <summary>
    /// A worker's stack. The solver recurses on the thread that calls it, and a new thread's default stack is smaller than
    /// the main thread's on Linux, so a worker gets more than either platform gives the main thread: a pair that fits when
    /// it is verified alone fits here.
    /// </summary>
    internal const int StackBytes = 16 * 1024 * 1024;

    /// <summary>How many threads <paramref name="jobs"/> allows for <paramref name="items"/> items that reach the backend: never more than the items, and at least one.</summary>
    public static int Count(int jobs, int items) => Math.Clamp(items, 1, jobs);

    /// <summary>
    /// The most a check that answers was measured to slow down beside the other workers, rounded up (ticket P2-132
    /// criterion 5): 5.74 times on 24 threads of a 24-core machine (<c>docs/runs/2026-10-09-jobs-scaling.md</c>). The
    /// threads share memory and caches even when each has a processor of its own.
    /// </summary>
    internal const int Slowdown = 6;

    /// <summary>
    /// <paramref name="options"/> for <paramref name="workers"/> threads on <paramref name="processors"/> processors
    /// (the machine's, unless a test gives another): the wall-clock backstop (<see cref="VerificationOptions.TimeoutMs"/>)
    /// times <see cref="Slowdown"/>, and times that again for each time over the threads outnumber the processors, since
    /// threads sharing a processor each get a share of it. It is never more than the backstop times the threads, which
    /// is what the threads would need if they shared one processor. So a query that ends within the backstop alone ends
    /// within this one here, and the deterministic resource limit, which no thread changes, is what ends it. One thread
    /// leaves the options as they are.
    /// </summary>
    public static VerificationOptions Sharing(VerificationOptions options, int workers, int? processors = null)
    {
        int cores = processors ?? Environment.ProcessorCount;
        long perProcessor = (workers + (long)cores - 1) / cores;
        return options with { TimeoutMs = (int)Math.Min(options.TimeoutMs * Math.Min(workers, Slowdown * perProcessor), int.MaxValue) };
    }

    /// <summary>
    /// <paramref name="work"/> of every index below <paramref name="count"/>, each result at its index. One worker runs
    /// them in order on the calling thread. More start them in order on threads of their own, each taking the next index
    /// when it is free. An exception <paramref name="work"/> lets out ends the run, as it does on one thread: no further
    /// item starts, those in flight finish, and the first exception is thrown again on the calling thread.
    /// </summary>
    public static T[] Run<T>(int count, int workers, Func<int, T> work)
    {
        T[] results = new T[count];
        if (workers <= 1)
        {
            for (int i = 0; i < count; i++)
            {
                results[i] = work(i);
            }

            return results;
        }

        int next = -1;
        ExceptionDispatchInfo? failure = null;
        Thread[] threads = [.. Enumerable.Range(0, workers).Select(_ => new Thread(Work, StackBytes) { IsBackground = true, Name = "equiv-pair-worker" })];
        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        failure?.Throw();
        return results;

        void Work()
        {
            try
            {
                for (int i = Interlocked.Increment(ref next); i < count && Volatile.Read(ref failure) is null; i = Interlocked.Increment(ref next))
                {
                    results[i] = work(i);
                }
            }
            catch (Exception exception) when (Keep(ref failure, exception))
            {
                // Kept for the calling thread. Nothing above a worker thread catches, so an exception that left it would
                // end the process without a result.
            }
        }
    }

    /// <summary>Keeps <paramref name="exception"/> as <paramref name="failure"/> unless an earlier one is there; always true, for a filter.</summary>
    private static bool Keep(ref ExceptionDispatchInfo? failure, Exception exception)
    {
        _ = Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(exception), comparand: null);
        return true;
    }
}
