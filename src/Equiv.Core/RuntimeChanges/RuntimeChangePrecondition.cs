namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// What a call's arguments must be able to be for a <see cref="RuntimeChange"/> row's documented change to reach it
/// (ticket P2-073). Each value reads the operands it names; <see cref="RuntimeChangeReach"/> has the rules.
/// </summary>
public enum RuntimeChangePrecondition
{
    /// <summary><c>caseInsensitivePattern</c>: the <c>pattern</c> turns on case-insensitive matching inline, or the <c>options</c> have <c>RegexOptions.IgnoreCase</c>.</summary>
    CaseInsensitivePattern,

    /// <summary><c>twoDigitYearFormat</c>: the <c>format</c> is a standard format, or has a <c>y</c> or <c>yy</c> specifier.</summary>
    TwoDigitYearFormat,

    /// <summary>
    /// <c>cultureSensitiveText</c>: the receiver or a string argument has a character that is not an ASCII letter or digit,
    /// and the call is no search for a character, whose every argument but the receiver is a <see cref="CallArgument.IsCharOrIndex"/>:
    /// that search is ordinal on every runtime (ticket P2-181).
    /// </summary>
    CultureSensitiveText,

    /// <summary><c>invalidPath</c>: a string argument is a path that .NET Framework's up-front validation could reject, and is not a <see cref="CallArgument.ValidPath"/>.</summary>
    InvalidPath,

    /// <summary><c>trimEntries</c>: the <c>options</c> are neither <c>None</c> nor <c>RemoveEmptyEntries</c>, the only two .NET Framework accepts (ticket P2-181).</summary>
    TrimEntries,

    /// <summary>
    /// <c>absentFolder</c>: the <c>folder</c> is neither <c>System</c> nor one of the folders .NET falls back to a Windows
    /// environment variable for, which every install has (ticket P2-189).
    /// </summary>
    AbsentFolder,

    /// <summary><c>blankFormat</c>: the <c>format</c> is null, empty or white space (ticket P2-189).</summary>
    BlankFormat,
}
