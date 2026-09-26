namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// An evaluated MSBuild property or expression (M3-029). <paramref name="Unsupported"/> names the construct that kept
/// it from being evaluated exactly, or is null when <paramref name="Text"/> is exact. Such a value only skips the
/// project when something the loader reads depends on it. <paramref name="ToolPath"/> is true when the value depends
/// on one of MSBuild's own tool paths, which the bare loader replaces with built-in knowledge; its
/// <paramref name="Text"/> is then the rest of the value (<c>\Microsoft.CSharp.targets</c>), enough to tell which import it is.
/// </summary>
internal sealed record PropertyValue(string Text, string? Unsupported = null, bool ToolPath = false)
{
    public static PropertyValue Empty { get; } = new(string.Empty);

    public static PropertyValue Poisoned(string construct) => new(string.Empty, construct);

    /// <summary>The exact text.</summary>
    /// <exception cref="UnsupportedConstructException">The value is not exact.</exception>
    public string Exact => Unsupported is null ? Text : throw new UnsupportedConstructException(Unsupported);
}
