using Equiv.Core;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// <see cref="PairWorkers"/> (ticket P2-077): how many threads a phase gets, the backstop they share, and that every
/// item's result is at its index whichever thread made it.
/// </summary>
public sealed class PairWorkersTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(24, 0, 1)]
    [InlineData(24, 1, 1)]
    [InlineData(24, 5, 5)]
    [InlineData(4, 5, 4)]
    [InlineData(4, 4, 4)]
    [InlineData(1, 5, 1)]
    public void Count_IsNeverMoreThanTheItemsOrTheJobsAndAtLeastOne(int jobs, int items, int workers)
    {
        Assert.Equal(workers, PairWorkers.Count(jobs, items));
    }

    /// <summary>Criterion 4: the backstop is the configured one times the threads, and nothing else changes.</summary>
    [Fact]
    public void Sharing_ScalesOnlyTheBackstop()
    {
        VerificationOptions options = new(3, 60_000, []) { ResourceLimit = 7 };

        Assert.Same(options.Log, PairWorkers.Sharing(options, 4).Log);
        Assert.Equal(options, PairWorkers.Sharing(options, 1));
        Assert.Equal(options with { TimeoutMs = 240_000 }, PairWorkers.Sharing(options, 4));
        Assert.Equal(int.MaxValue, PairWorkers.Sharing(options with { TimeoutMs = int.MaxValue / 2 }, 3).TimeoutMs);
    }

    [Fact]
    public void Run_OnOneWorker_StaysOnTheCallingThreadInOrder()
    {
        List<int> order = [];
        int caller = Environment.CurrentManagedThreadId;

        int[] threads = PairWorkers.Run(5, 1, i =>
        {
            order.Add(i);
            return Environment.CurrentManagedThreadId;
        });

        Assert.Equal([0, 1, 2, 3, 4], order);
        Assert.All(threads, thread => Assert.Equal(caller, thread));
        Assert.Empty(PairWorkers.Run(0, 1, static i => i));
        Assert.Empty(PairWorkers.Run(0, 4, static i => i));
    }

    /// <summary>Four workers are four threads at once, none of them the caller's, each with the larger stack, and the results are by index.</summary>
    [Fact]
    public void Run_OnSeveralWorkers_PutsEachResultAtItsIndex()
    {
        using Rendezvous together = new(4);
        int caller = Environment.CurrentManagedThreadId;
        HashSet<int> threads = [];

        int[] squares = PairWorkers.Run(40, 4, i =>
        {
            together.Meet();
            lock (threads)
            {
                threads.Add(Environment.CurrentManagedThreadId);
            }

            together.Leave(i);
            return i * i;
        });

        Assert.Equal(Enumerable.Range(0, 40).Select(static i => i * i), squares);
        Assert.Equal(4, threads.Count);
        Assert.DoesNotContain(caller, threads);
        Assert.Equal(16 * 1024 * 1024, PairWorkers.StackBytes);
    }

    /// <summary>
    /// An exception a worker lets out reaches the caller as itself, and no item starts after it. The first item fails only
    /// once the second is in flight, and the second worker is held in its item until the first worker's thread has ended,
    /// so the failure is there when it looks for its next item.
    /// </summary>
    [Fact]
    public void Run_RethrowsTheFirstFailureAndStartsNothingAfterIt()
    {
        using ManualResetEventSlim failing = new(initialState: false);
        using ManualResetEventSlim secondInFlight = new(initialState: false);
        Thread? failed = null;
        List<int> started = [];

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => PairWorkers.Run(6, 2, i =>
        {
            lock (started)
            {
                started.Add(i);
            }

            if (i == 0)
            {
                Assert.True(secondInFlight.Wait(Patience, TestContext.Current.CancellationToken));
                failed = Thread.CurrentThread;
                failing.Set();
                throw new InvalidOperationException("first");
            }

            secondInFlight.Set();
            Assert.True(failing.Wait(Patience, TestContext.Current.CancellationToken));
            Assert.True(failed!.Join(Patience));
            return i;
        }));

        Assert.Equal("first", thrown.Message);
        Assert.Equal([0, 1], started.Order());
    }
}
