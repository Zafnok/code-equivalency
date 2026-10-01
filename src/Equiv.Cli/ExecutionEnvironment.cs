using Equiv.Core;
using Equiv.Execute;

namespace Equiv.Cli;

/// <summary>
/// Where <c>--execute</c> runs (ADR 0035; tickets M4-009, P2-056): whether the OS is Windows, which only a side on .NET
/// Framework needs (ADR 0040 decision 3), and the host that starts driver processes. Unit tests pass a fake host and either
/// OS. <see cref="Time"/> is the clock testing an Unknown pair measures its budget by (ticket P1-008).
/// </summary>
internal sealed record ExecutionEnvironment(bool IsWindows, IDriverHost Host)
{
    public TimeProvider Time { get; init; } = TimeProvider.System;

    public const string Note = "note: --execute runs code from both solutions on this machine, in a temporary working directory; it is not sandboxed, so absolute paths, the registry and the network are still reachable";

    public static ExecutionEnvironment Current => new(OperatingSystem.IsWindows(), new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes));

    /// <summary>
    /// The error <c>--execute</c> stops with on this OS, naming the first project of <paramref name="analysis"/> that runs on
    /// .NET Framework and its runtime; or null when every side can run here (<see cref="WindowsRequirement"/>).
    /// </summary>
    public string? Refusal(FrontendAnalysis analysis) =>
        WindowsRequirement.Refusal(IsWindows, [.. analysis.LegacyRuntimes.Select(static r => (r.Project, r.Runtime)), .. analysis.ModernRuntimes.Select(static r => (r.Project, r.Runtime))]) is { } refusal
            ? "error: --execute: " + refusal
            : null;
}
