using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// A solution filter (<c>.slnf</c>) that opens only the projects a solution builds (P2-013): its
/// <paramref name="Json"/> and the names of the projects it leaves out, <paramref name="NotBuilt"/>.
/// </summary>
internal sealed record SolutionFilter(string Json, ImmutableArray<string> NotBuilt);
