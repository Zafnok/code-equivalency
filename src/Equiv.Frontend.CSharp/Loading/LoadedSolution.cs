using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A loaded side. <paramref name="Compilations"/> are the C# projects that loaded cleanly enough to bind;
/// <paramref name="Diagnostics"/> holds what did not skip one of them: workspace warnings and compiler errors outside
/// the unresolved-reference family. <paramref name="Skipped"/> lists every project that was left out (ADR 0029).
/// <see cref="NotBuilt"/> names the projects the solution's default configuration does not build, which were never
/// opened and count neither as loaded nor as skipped (P2-013). <see cref="Runtimes"/> gives each loaded project its runtime
/// (ADR 0040; P2-053); the frontend fills it once the side's configured runtime is known.
/// </summary>
internal sealed record LoadedSolution(
    Solution Solution,
    ImmutableArray<Compilation> Compilations,
    ImmutableArray<LoadDiagnostic> Diagnostics,
    ImmutableArray<SkippedProject> Skipped)
{
    public ImmutableArray<string> NotBuilt { get; init; } = [];

    public ImmutableArray<ProjectRuntime> Runtimes { get; init; } = [];
}
