namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// The check a path row's .NET Framework member makes of its path before it does anything (ticket P2-231), as the reference
/// source reads; <c>runtime-changes.json</c>'s <c>frameworkCheck</c>. Each value rejects what the one before it does and
/// more, and only <see cref="FullPath"/> rejects everything <see cref="RuntimeChangePrecondition.InvalidPath"/> names.
/// </summary>
public enum PathCheck
{
    /// <summary><c>pathCharacters</c>: <c>Path.CheckInvalidPathChars</c> alone, which rejects a control character and <c>" &lt; &gt; |</c>.</summary>
    PathCharacters,

    /// <summary><c>limitedNormalization</c>: that check, and <c>NormalizePath</c> without its full check, which adds a blank path and a long one.</summary>
    LimitedNormalization,

    /// <summary><c>fullPath</c>: <c>NormalizePath</c>'s full check and a <c>FileIOPermission</c> demand, which add a wildcard and a stray colon.</summary>
    FullPath,
}
