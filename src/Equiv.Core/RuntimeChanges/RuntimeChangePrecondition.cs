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

    /// <summary><c>cultureSensitiveText</c>: the receiver or a string argument has a character that is not an ASCII letter or digit.</summary>
    CultureSensitiveText,

    /// <summary><c>invalidPath</c>: a string argument is a path that .NET Framework's up-front validation could reject.</summary>
    InvalidPath,
}
