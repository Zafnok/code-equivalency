using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// Whether a call can reach the change a row with a <see cref="RuntimeChangePrecondition"/> documents (ticket P2-073).
/// A precondition reads some of the call's operands. The call cannot reach the change only when it has at least one of
/// them, every one is a compile-time constant, and no constant meets the precondition; a call with none of them, or
/// with one that is not a constant, can.
/// </summary>
internal static partial class RuntimeChangeReach
{
    /// <summary><c>RegexOptions.IgnoreCase</c>.</summary>
    private const int IgnoreCase = 1;

    /// <summary>The longest path .NET Framework accepts for a directory without the long-path switch.</summary>
    private const int LongPath = 248;

    /// <summary>The characters .NET Framework rejects in a path, wildcards included; a control character is one too.</summary>
    private const string InvalidPathCharacters = "\"<>|?*";

    public static bool CanReach(RuntimeChangePrecondition precondition, ImmutableArray<CallArgument> arguments)
    {
        ImmutableArray<CallArgument> read = [.. arguments.Where(a => Reads(precondition, a))];
        return read.IsEmpty || read.Any(a => !a.IsConstant || Meets(precondition, a.Value));
    }

    private static bool Reads(RuntimeChangePrecondition precondition, CallArgument argument) => precondition switch
    {
        RuntimeChangePrecondition.CaseInsensitivePattern => argument.Parameter is "pattern" or "options",
        RuntimeChangePrecondition.TwoDigitYearFormat => argument.Parameter is "format",
        _ => argument.IsString,
    };

    private static bool Meets(RuntimeChangePrecondition precondition, object? value) => (precondition, value) switch
    {
        (RuntimeChangePrecondition.CaseInsensitivePattern, string pattern) => InlineIgnoreCase.IsMatch(pattern),
        (RuntimeChangePrecondition.CaseInsensitivePattern, int options) => (options & IgnoreCase) != 0,
        (RuntimeChangePrecondition.TwoDigitYearFormat, string format) => format.Length < 2 || ShortYear.IsMatch(format),
        (RuntimeChangePrecondition.CultureSensitiveText, string text) => !text.All(char.IsAsciiLetterOrDigit),
        (RuntimeChangePrecondition.InvalidPath, string path) => IsSuspectPath(path),
        _ => true,
    };

    /// <summary>
    /// Whether .NET Framework's validation could reject <paramref name="path"/>: it is blank or long, or it has a
    /// control character, one of <see cref="InvalidPathCharacters"/>, or a colon anywhere but after a drive letter.
    /// </summary>
    private static bool IsSuspectPath(string path) =>
        string.IsNullOrWhiteSpace(path)
        || path.Length >= LongPath
        || path.Where(static (c, i) => c < ' ' || InvalidPathCharacters.Contains(c, StringComparison.Ordinal) || (c == ':' && i != 1)).Any();

    /// <summary>An inline option group that turns <c>i</c> on: <c>(?i)</c>, <c>(?i:</c>, <c>(?mi-s)</c>.</summary>
    [GeneratedRegex(@"\(\?[a-z]*i", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineIgnoreCase { get; }

    /// <summary>A run of one or two <c>y</c>: the year specifiers that parse a two-digit year.</summary>
    [GeneratedRegex("(?<!y)y{1,2}(?!y)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShortYear { get; }
}
