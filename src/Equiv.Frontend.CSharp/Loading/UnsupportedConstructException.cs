namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The bare evaluator met MSBuild it cannot evaluate exactly (M3-029, ADR 0029). The project is skipped with a
/// diagnostic naming <see cref="Construct"/>; it is never loaded approximately.
/// </summary>
internal sealed class UnsupportedConstructException(string construct) : Exception($"the bare loader cannot evaluate {construct} exactly")
{
    public string Construct { get; } = construct;
}
