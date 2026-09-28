using Equiv.Execute;

namespace Equiv.Cli;

/// <summary>
/// Where <c>--execute</c> runs (ADR 0035; ticket M4-009): whether the OS is Windows, which the .NET Framework 4.8 side
/// needs, and the host that starts driver processes. Unit tests pass a fake host and either OS. <see cref="Time"/> is the
/// clock testing an Unknown pair measures its budget by (ticket P1-008).
/// </summary>
internal sealed record ExecutionEnvironment(bool IsWindows, IDriverHost Host)
{
    public TimeProvider Time { get; init; } = TimeProvider.System;

    public const string NeedsWindows = "error: --execute needs Windows and .NET Framework 4.8 (ADR 0035)";

    public const string Note = "note: --execute runs code from both solutions on this machine, in a temporary working directory; it is not sandboxed, so absolute paths, the registry and the network are still reachable";

    public static ExecutionEnvironment Current => new(OperatingSystem.IsWindows(), new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes));
}
