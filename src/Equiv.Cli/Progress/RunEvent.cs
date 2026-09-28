using Equiv.Core.Progress;

namespace Equiv.Cli.Progress;

/// <summary>One <see cref="IRunLog"/> call, stamped with its <see cref="TimeProvider"/> timestamp, on its way to <see cref="ChannelRunLog"/>'s writer.</summary>
internal abstract record RunEvent(long Timestamp)
{
    internal sealed record PhaseStarted(long Timestamp, string Name, int Total, long TotalWeight, PhaseBound? Bound) : RunEvent(Timestamp);

    internal sealed record ItemStarted(long Timestamp, string Identity, long Weight) : RunEvent(Timestamp);

    internal sealed record ItemFinished(long Timestamp, string Outcome) : RunEvent(Timestamp);

    internal sealed record DetailWritten(long Timestamp, string Text) : RunEvent(Timestamp);

    internal sealed record PhaseFinished(long Timestamp) : RunEvent(Timestamp);
}
