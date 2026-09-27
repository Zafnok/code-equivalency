using System.Globalization;
using System.Text;

namespace Equiv.Cli.Tests.Progress;

/// <summary>
/// A <see cref="TextWriter"/> that keeps each line <see cref="WriteLine(string)"/> is given, lets a test wait for one, and,
/// once <see cref="Hold"/> is called, blocks the writer inside its next line until <see cref="Release"/>.
/// </summary>
internal sealed class LineWriter : TextWriter
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly object gate = new();
    private readonly List<string> lines = [];
    private readonly ManualResetEventSlim open = new(initialState: true);
    private readonly ManualResetEventSlim entered = new(initialState: false);

    public override Encoding Encoding => Encoding.UTF8;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (gate)
            {
                return [.. lines];
            }
        }
    }

    public void Hold() => open.Reset();

    public void Release() => open.Set();

    /// <summary>Waits until a line is being written while <see cref="Hold"/> is in force.</summary>
    public void WaitUntilBlocked()
    {
        if (!entered.Wait(Patience, TestContext.Current.CancellationToken))
        {
            throw new TimeoutException("the writer never blocked");
        }
    }

    public override void WriteLine(string? value)
    {
        if (!open.IsSet)
        {
            entered.Set();
            open.Wait(TestContext.Current.CancellationToken);
        }

        lock (gate)
        {
            lines.Add(value ?? string.Empty);
            Monitor.PulseAll(gate);
        }
    }

    public override void Write(char value) => throw new NotSupportedException("the run log writes whole lines");

    /// <summary>Waits until there are at least <paramref name="count"/> lines, and returns them.</summary>
    public IReadOnlyList<string> WaitFor(int count)
    {
        DateTime deadline = DateTime.UtcNow + Patience;
        lock (gate)
        {
            while (lines.Count < count)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !Monitor.Wait(gate, left))
                {
                    throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"expected {count} lines, got {lines.Count}: {string.Join(" | ", lines)}"));
                }
            }

            return [.. lines];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            open.Dispose();
            entered.Dispose();
        }

        base.Dispose(disposing);
    }
}
