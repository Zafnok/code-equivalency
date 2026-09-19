using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>The only code that touches MSBuild: creating the workspace and opening the solution.</summary>
[ExcludeFromCodeCoverage(Justification = "M2-001: spawns the MSBuild build host; covered by Equiv.Tests.Integration")]
internal static class MsBuildWorkspaceFactory
{
    private static readonly Dictionary<string, string> Properties = new(StringComparer.Ordinal)
    {
        ["Configuration"] = "Debug",
        ["Platform"] = "AnyCPU",
    };

    public static Workspace Create() => MSBuildWorkspace.Create(Properties);

    public static Task<Solution> OpenSolutionAsync(Workspace workspace, string solutionPath, CancellationToken ct) =>
        ((MSBuildWorkspace)workspace).OpenSolutionAsync(solutionPath, cancellationToken: ct);
}
