namespace Equiv.Frontend.CSharp.Loading;

/// <summary>Loads one side of a comparison into Roslyn compilations (ADR 0004).</summary>
internal interface ISolutionLoader
{
    /// <exception cref="SolutionLoadException">The solution could not be loaded completely.</exception>
    Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct);
}
