using Equiv.Core.Progress;

namespace Equiv.Cli.Progress;

/// <summary>
/// One <see cref="IRunLog"/> call, stamped with its <see cref="TimeProvider"/> timestamp, on its way to <see cref="ChannelRunLog"/>'s writer.
/// An item's end and a detail carry the item they are about, since several items may be in flight (ticket P2-077).
/// </summary>
internal abstract record RunEvent(long Timestamp)
{
    internal sealed record PhaseStarted(long Timestamp, string Name, int Total, long TotalWeight, PhaseBound? Bound) : RunEvent(Timestamp);

    internal sealed record ItemFinished(long Timestamp, InFlight Item, string Outcome) : RunEvent(Timestamp);

    internal sealed record DetailWritten(long Timestamp, string Text, string? Item) : RunEvent(Timestamp);

    internal sealed record PhaseFinished(long Timestamp) : RunEvent(Timestamp);
}
