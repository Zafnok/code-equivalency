using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A loaded side. <paramref name="Compilations"/> are the C# projects that loaded cleanly enough to bind;
/// <paramref name="Diagnostics"/> holds what did not skip one of them: workspace warnings and compiler errors outside
/// the unresolved-reference family. <paramref name="Skipped"/> lists every project that was left out (ADR 0029).
/// </summary>
internal sealed record LoadedSolution(
    Solution Solution,
    ImmutableArray<Compilation> Compilations,
    ImmutableArray<LoadDiagnostic> Diagnostics,
    ImmutableArray<SkippedProject> Skipped);
