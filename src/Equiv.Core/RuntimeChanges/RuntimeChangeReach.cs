using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// Whether a call can reach the change a row with a <see cref="RuntimeChangePrecondition"/> documents (ticket P2-073).
/// A precondition reads some of the call's operands. The call cannot reach the change only when it has at least one of
/// them, every one is a compile-time constant, and no constant meets the precondition; a call with none of them, or
/// with one that is not a constant, can. An operand that is a <see cref="CallArgument.ValidPath"/> never meets
/// <see cref="RuntimeChangePrecondition.InvalidPath"/>, constant or not (ticket P2-074), and nor does a
/// <see cref="CallArgument.ValidPathOrEmpty"/> on a row the empty string does not reach (ticket P2-232). A search for a character, whose
/// every argument but the receiver is a <see cref="CallArgument.IsCharOrIndex"/>, never meets
/// <see cref="RuntimeChangePrecondition.CultureSensitiveText"/>, whatever its receiver: it is ordinal on every runtime
/// (ticket P2-181).
/// </summary>
internal static partial class RuntimeChangeReach
{
    /// <summary><c>RegexOptions.IgnoreCase</c>.</summary>
    private const int IgnoreCase = 1;

    /// <summary><c>StringSplitOptions.None</c>.</summary>
    private const int None = 0;

    /// <summary><c>StringSplitOptions.RemoveEmptyEntries</c>.</summary>
    private const int RemoveEmptyEntries = 1;

    /// <summary>
    /// The <c>Environment.SpecialFolder</c> values every Windows install has (ticket P2-189): <c>System</c>, which .NET
    /// assumes always exists, and the folders it falls back to a Windows environment variable for when the shell cannot
    /// give their path (dotnet/runtime <c>Environment.Windows.cs</c>, <c>GetFolderPathCore</c>).
    /// </summary>
    private static readonly FrozenSet<int> PresentFolders = new[]
    {
        26, // ApplicationData, %APPDATA%
        28, // LocalApplicationData, %LOCALAPPDATA%
        35, // CommonApplicationData, %ProgramData%
        36, // Windows, %windir%
        37, // System
        38, // ProgramFiles, %ProgramFiles%
        40, // UserProfile, %USERPROFILE%
        42, // ProgramFilesX86, %ProgramFiles(x86)%
        43, // CommonProgramFiles, %CommonProgramFiles%
        44, // CommonProgramFilesX86, %CommonProgramFiles(x86)%
    }.ToFrozenSet();

    /// <summary>The longest path .NET Framework accepts for a directory without the long-path switch.</summary>
    private const int LongPath = 248;

    /// <summary>The characters .NET Framework rejects in a path, wildcards included; a control character is one too.</summary>
    private const string InvalidPathCharacters = "\"<>|?*";

    public static bool CanReach(RuntimeChangePrecondition precondition, ImmutableArray<CallArgument> arguments) =>
        CanReach(precondition, arguments, emptyPathUnchanged: false);

    /// <summary>
    /// As the two-argument overload, for a row whose change the empty string does not reach when
    /// <paramref name="emptyPathUnchanged"/> is true (ticket P2-232): an operand that is a
    /// <see cref="CallArgument.ValidPathOrEmpty"/> then never meets <see cref="RuntimeChangePrecondition.InvalidPath"/>.
    /// </summary>
    public static bool CanReach(RuntimeChangePrecondition precondition, ImmutableArray<CallArgument> arguments, bool emptyPathUnchanged)
    {
        if (precondition == RuntimeChangePrecondition.CultureSensitiveText && IsCharacterSearch(arguments))
        {
            return false;
        }

        ImmutableArray<CallArgument> read = [.. arguments.Where(a => Reads(precondition, a))];
        return read.IsEmpty || read.Any(a => !IsKnownValid(precondition, a, emptyPathUnchanged) && (!a.IsConstant || Meets(precondition, a.Value)));
    }

    /// <summary>A call that passes something besides its receiver, and only characters, positions and counts: <c>s.IndexOf('/', 1)</c>.</summary>
    private static bool IsCharacterSearch(ImmutableArray<CallArgument> arguments)
    {
        ImmutableArray<CallArgument> passed = [.. arguments.Where(static a => !string.Equals(a.Parameter, CallArgument.Receiver, StringComparison.Ordinal))];
        return !passed.IsEmpty && passed.All(static a => a.IsCharOrIndex);
    }

    private static bool IsKnownValid(RuntimeChangePrecondition precondition, CallArgument argument, bool emptyPathUnchanged) =>
        precondition == RuntimeChangePrecondition.InvalidPath && (argument.ValidPath || (emptyPathUnchanged && argument.ValidPathOrEmpty));

    private static bool Reads(RuntimeChangePrecondition precondition, CallArgument argument) => precondition switch
    {
        RuntimeChangePrecondition.CaseInsensitivePattern => argument.Parameter is "pattern" or "options",
        RuntimeChangePrecondition.TwoDigitYearFormat => argument.Parameter is "format",
        RuntimeChangePrecondition.TrimEntries => argument.Parameter is "options",
        RuntimeChangePrecondition.AbsentFolder => argument.Parameter is "folder",
        RuntimeChangePrecondition.BlankFormat => argument.Parameter is "format",
        _ => argument.IsString,
    };

    private static bool Meets(RuntimeChangePrecondition precondition, object? value) => (precondition, value) switch
    {
        (RuntimeChangePrecondition.CaseInsensitivePattern, string pattern) => InlineIgnoreCase.IsMatch(pattern),
        (RuntimeChangePrecondition.CaseInsensitivePattern, int options) => (options & IgnoreCase) != 0,
        (RuntimeChangePrecondition.TwoDigitYearFormat, string format) => format.Length < 2 || ShortYear.IsMatch(format),
        (RuntimeChangePrecondition.CultureSensitiveText, string text) => !text.All(char.IsAsciiLetterOrDigit),
        (RuntimeChangePrecondition.InvalidPath, string path) => IsSuspectPath(path),
        (RuntimeChangePrecondition.TrimEntries, int options) => options is not (None or RemoveEmptyEntries),
        (RuntimeChangePrecondition.AbsentFolder, int folder) => !PresentFolders.Contains(folder),
        (RuntimeChangePrecondition.BlankFormat, string format) => string.IsNullOrWhiteSpace(format),
        _ => true,
    };

    /// <summary>
    /// Whether .NET Framework's validation could reject <paramref name="path"/>: it is blank or long, or it has a
    /// control character, one of <see cref="InvalidPathCharacters"/>, or a colon anywhere but after a drive letter.
    /// </summary>
    public static bool IsSuspectPath(string path) =>
        string.IsNullOrWhiteSpace(path)
        || path.Length >= LongPath
        || path.Where(static (c, i) => IsRejected(c) || (c == ':' && i != 1)).Any();

    /// <summary>
    /// Whether <paramref name="text"/> has no character .NET Framework's validation rejects and no colon, so joining it to a
    /// valid path adds no such character and puts no colon off a drive letter (ticket P2-208).
    /// </summary>
    public static bool IsPathPart(string text) => !text.Any(static c => c == ':' || IsRejected(c));

    /// <summary>A control character or one of <see cref="InvalidPathCharacters"/>.</summary>
    private static bool IsRejected(char c) => c < ' ' || InvalidPathCharacters.Contains(c, StringComparison.Ordinal);

    /// <summary>An inline option group that turns <c>i</c> on: <c>(?i)</c>, <c>(?i:</c>, <c>(?mi-s)</c>.</summary>
    [GeneratedRegex(@"\(\?[a-z]*i", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineIgnoreCase { get; }

    /// <summary>A run of one or two <c>y</c>: the year specifiers that parse a two-digit year.</summary>
    [GeneratedRegex("(?<!y)y{1,2}(?!y)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShortYear { get; }
}
