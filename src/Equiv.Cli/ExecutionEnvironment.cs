using Equiv.Execute;

namespace Equiv.Cli;

/// <summary>
/// Where <c>--execute</c> runs (ADR 0035; ticket M4-009): whether the OS is Windows, which the .NET Framework 4.8 side
/// needs, and the host that starts driver processes. Unit tests pass a fake host and either OS.
/// </summary>
internal sealed record ExecutionEnvironment(bool IsWindows, IDriverHost Host)
{
    public const string NeedsWindows = "error: --execute needs Windows and .NET Framework 4.8 (ADR 0035)";

    public const string Note = "note: --execute runs code from both solutions on this machine";

    public static ExecutionEnvironment Current => new(OperatingSystem.IsWindows(), new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes));
}
