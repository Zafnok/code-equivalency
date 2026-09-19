using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>A partial load, turned into a hard failure with every diagnostic attached.</summary>
internal sealed class SolutionLoadException(string path, ImmutableArray<LoadDiagnostic> diagnostics)
    : Exception($"failed to load '{path}': {string.Join("; ", diagnostics.Select(static d => $"{d.Kind} {d.Id} {d.Project}: {d.Message}"))}")
{
    public string Path { get; } = path;

    public ImmutableArray<LoadDiagnostic> Diagnostics { get; } = diagnostics;
}
