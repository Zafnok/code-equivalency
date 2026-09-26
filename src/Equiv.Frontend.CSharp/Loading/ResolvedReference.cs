using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>A metadata reference the bare loader resolved to a file (M3-029), with the <c>Reference</c> item's aliases and interop switch.</summary>
internal sealed record ResolvedReference(string Path, ImmutableArray<string> Aliases, bool EmbedInteropTypes)
{
    public static ResolvedReference Plain(string path) => new(path, [], EmbedInteropTypes: false);
}
