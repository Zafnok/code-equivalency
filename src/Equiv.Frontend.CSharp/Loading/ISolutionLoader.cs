using Equiv.Core.Verdicts;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>Loads one side of a comparison into Roslyn compilations (ADR 0004).</summary>
internal interface ISolutionLoader
{
    /// <summary>
    /// Loads the solution as <paramref name="side"/> of the comparison. The side decides whether a project whose source
    /// names types its references do not provide is skipped (legacy) or kept, to be decided method by method (modern;
    /// ADR 0029 as clarified by ticket P2-085).
    /// </summary>
    /// <exception cref="SolutionLoadException">The solution could not be loaded completely.</exception>
    Task<LoadedSolution> LoadAsync(string solutionPath, Codebase side, CancellationToken ct);
}
