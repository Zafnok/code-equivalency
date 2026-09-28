namespace Equiv.Cli.Progress;

/// <summary>
/// <c>--verbosity</c> (ADR 0038). <see cref="Quiet"/> writes no progress; <see cref="Normal"/> writes each phase's start
/// and end, a line at most every 5% of its weight, and a heartbeat every minute; <see cref="Debug"/> also writes one line
/// per item and every detail, with a heartbeat every 10 seconds.
/// </summary>
internal enum Verbosity
{
    Quiet,
    Normal,
    Debug,
}
