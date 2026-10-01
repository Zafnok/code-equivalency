using Equiv.Core;

namespace Equiv.Execute;

/// <summary>
/// Whether execution can run here (ADR 0040 decision 3; ticket P2-056): Windows is needed only when some side runs on .NET
/// Framework. <c>compare --execute</c>, <c>equiv mcp</c>'s <c>probe</c> and <c>runtime-diff</c> all ask this one check.
/// </summary>
public static class WindowsRequirement
{
    /// <summary>
    /// Why <paramref name="runtimes"/> cannot run on this OS, naming the first project on .NET Framework and its runtime; or
    /// null when they can. A runtime is as <c>run.properties.runtimes</c> reports it, and a project hosted on several runs on
    /// the first; one with no runtime (an unhosted <c>netstandard</c> project) needs nothing.
    /// </summary>
    public static string? Refusal(bool isWindows, IEnumerable<(string Project, string Runtime)> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        return isWindows
            ? null
            : runtimes.Where(static r => TargetRuntime.Parse(r.Runtime.Split(',')[0]) is { Family: TargetRuntime.RuntimeFamily.NetFramework })
                .Select(static r => $"{r.Project} runs on {r.Runtime.Split(',')[0]}, and .NET Framework needs Windows (ADR 0040)")
                .FirstOrDefault();
    }
}
