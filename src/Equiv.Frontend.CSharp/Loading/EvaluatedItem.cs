using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// One item the bare loader reads (M3-029). <paramref name="Include"/> is a full path for <c>Compile</c> and
/// <c>ProjectReference</c> items and the item's own text for <c>Reference</c> and <c>PackageReference</c>.
/// <paramref name="Metadata"/> holds only the metadata the loader reads, already expanded.
/// </summary>
internal sealed record EvaluatedItem(string Type, string Include, ImmutableDictionary<string, string> Metadata)
{
    public string? Metadatum(string name) => Metadata.TryGetValue(name, out string? value) ? value : null;
}
