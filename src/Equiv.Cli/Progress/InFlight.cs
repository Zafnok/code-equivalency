namespace Equiv.Cli.Progress;

/// <summary>An item of a phase that has started and not finished: when it started, its identity and its weight (ticket P2-077).</summary>
internal sealed record InFlight(long Started, string Identity, long Weight);