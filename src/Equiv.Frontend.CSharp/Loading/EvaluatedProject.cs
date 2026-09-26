using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>A non-SDK project as the bare evaluator sees it (M3-029): its final properties and the items the loader reads.</summary>
internal sealed record EvaluatedProject(string Path, MsBuildProperties Properties, ImmutableArray<EvaluatedItem> Items)
{
    public string Directory => System.IO.Path.GetDirectoryName(Path)!;

    public IEnumerable<EvaluatedItem> OfType(string type) => Items.Where(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase));
}
