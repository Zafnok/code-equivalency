using System.Globalization;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// Holds each of its first <paramref name="count"/> callers until all of them have arrived, so a test passes only if that
/// many calls are in flight at once (ticket P2-077). Later callers pass straight through. <see cref="Leave"/> then lets
/// those first callers go in the reverse of the order they were started in, so the last started answers first.
/// </summary>
internal sealed class Rendezvous(int count) : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly Barrier barrier = new(count);
    private readonly ManualResetEventSlim[] left = [.. Enumerable.Range(0, count).Select(static _ => new ManualResetEventSlim(initialState: false))];
    private int arrived;

    public void Meet()
    {
        if (Interlocked.Increment(ref arrived) <= count && !barrier.SignalAndWait(Patience, TestContext.Current.CancellationToken))
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"{count} calls were never in flight at once"));
        }
    }

    /// <summary>
    /// Returns once the caller working on item <paramref name="index"/> may answer: at once for the last of the first
    /// <paramref name="count"/> items and for every later one, and otherwise when item <paramref name="index"/> + 1 has left.
    /// </summary>
    public void Leave(int index)
    {
        if (index >= count)
        {
            return;
        }

        if (index + 1 < count && !left[index + 1].Wait(Patience, TestContext.Current.CancellationToken))
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"item {index + 1} never left"));
        }

        left[index].Set();
    }

    public void Dispose()
    {
        barrier.Dispose();
        foreach (ManualResetEventSlim one in left)
        {
            one.Dispose();
        }
    }
}
