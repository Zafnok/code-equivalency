using System.Globalization;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>
/// Holds each of its first <paramref name="count"/> callers until all of them have arrived, so a test passes only if that
/// many calls are in flight at once (ticket P2-077). Later callers pass straight through.
/// </summary>
internal sealed class Rendezvous(int count) : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly Barrier barrier = new(count);
    private int arrived;

    public void Meet()
    {
        if (Interlocked.Increment(ref arrived) <= count && !barrier.SignalAndWait(Patience, TestContext.Current.CancellationToken))
        {
            throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"{count} calls were never in flight at once"));
        }
    }

    public void Dispose() => barrier.Dispose();
}
