using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A fully loaded side. <paramref name="Diagnostics"/> holds what did not abort the load:
/// workspace warnings and compiler errors outside the unresolved-reference family.
/// </summary>
internal sealed record LoadedSolution(
    Solution Solution,
    ImmutableArray<Compilation> Compilations,
    ImmutableArray<LoadDiagnostic> Diagnostics);
