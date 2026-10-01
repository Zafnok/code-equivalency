using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RuntimeChangesBackfill;

/// <summary>
/// One-off backfill of <c>changedIn</c> on every <c>runtime-changes.json</c> row (ticket P2-054; ADR 0040 decision 2).
/// Run once by hand; not run in CI. Every row gets <c>changedIn</c>, the target framework moniker of the first
/// runtime whose behaviour differs:
/// <list type="bullet">
/// <item>a curated row gets the version its reason names ("in .NET Core 3.0"), else its URL's;</item>
/// <item>a <c>compatibility/&lt;v&gt;</c> URL gets <c>net&lt;v&gt;</c> (<c>netcoreapp3.0</c> for 3.0);</item>
/// <item><c>unsupported-apis</c> and <c>fx-core</c> URLs get <c>netcoreapp1.0</c>, the .NET Framework to .NET boundary;</item>
/// <item>a measured row whose reason is .NET Framework's upfront path-character validation gets <c>netcoreapp1.0</c>;</item>
/// <item>anything else stays null (unknown: the row applies whenever the runtimes differ) and is reported.</item>
/// </list>
/// The root array becomes <c>{ "coveredFrom": "netcoreapp3.0", "rows": [...] }</c>. Rows keep their formatting: the
/// backfill only inserts one line per row and indents the array. It refuses a file that already has <c>coveredFrom</c>.
/// </summary>
internal static partial class Backfill
{
    private const string Boundary = "netcoreapp1.0";
    private const string CoveredFrom = "netcoreapp3.0";

    [GeneratedRegex(@"\bin \.NET (Core )?(?<version>\d+(\.\d+)?)\b", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReasonVersion { get; }

    [GeneratedRegex(@"core/compatibility/([a-z-]+/)?(?<version>\d+(\.\d+)?)[/#]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlVersion { get; }

    [GeneratedRegex("core/compatibility/(unsupported-apis|fx-core)#", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BoundaryUrl { get; }

    [GeneratedRegex("path characters|character validation", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PathValidation { get; }

    [GeneratedRegex(@"^(?<indent>\s*)""source"": ", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SourceLine { get; }

    /// <summary>Backfills the file named by the one argument in place and writes each row left null to <paramref name="output"/>.</summary>
    internal static int Run(string[] args, TextWriter output)
    {
        if (args.Length != 1)
        {
            output.WriteLine("usage: runtime-changes-backfill <path to runtime-changes.json>");
            return 1;
        }

        Result result = Apply(File.ReadAllLines(args[0]));
        File.WriteAllText(args[0], string.Join("\r\n", result.Lines) + "\r\n");
        foreach (string member in result.Unplaced)
        {
            output.WriteLine($"null: {member}");
        }

        return 0;
    }

    /// <summary>The backfilled file for <paramref name="lines"/>, and the members of the rows it could not place.</summary>
    internal static Result Apply(string[] lines)
    {
        if (lines.Any(static line => line.Contains("\"coveredFrom\"", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("runtime-changes.json already has coveredFrom; the backfill has run");
        }

        int open = Array.IndexOf(lines, "[");
        int close = Array.LastIndexOf(lines, "]");
        using JsonDocument document = JsonDocument.Parse(string.Join('\n', lines[open..(close + 1)]));
        JsonElement[] rows = [.. document.RootElement.EnumerateArray()];
        string?[] values = [.. rows.Select(static row => ChangedIn(Text(row, "source"), Text(row, "reason"), Text(row, "url")))];

        List<string> output = [.. lines[..open], "{", $"  \"coveredFrom\": \"{CoveredFrom}\",", "  \"rows\": ["];
        int placed = 0;
        foreach (string line in lines[(open + 1)..close])
        {
            output.Add("  " + line);
            Match source = SourceLine.Match(line);
            if (source.Success)
            {
                string json = values[placed] is { } value ? $"\"{value}\"" : "null";
                output.Add($"  {source.Groups["indent"].Value}\"changedIn\": {json},");
                placed++;
            }
        }

        output.Add("  ]");
        output.Add("}");
        return new Result(output, [.. rows.Where((_, index) => values[index] is null).Select(static row => Text(row, "member"))]);
    }

    /// <summary>The change point of a row with this <paramref name="source"/>, <paramref name="reason"/> and <paramref name="url"/>, or null.</summary>
    internal static string? ChangedIn(string source, string reason, string url)
    {
        Match named = ReasonVersion.Match(reason);
        if (string.Equals(source, "curated", StringComparison.Ordinal) && named.Success)
        {
            return Moniker(named.Groups["version"].Value);
        }

        Match linked = UrlVersion.Match(url);
        if (linked.Success)
        {
            return Moniker(linked.Groups["version"].Value);
        }

        bool boundary = BoundaryUrl.IsMatch(url)
            || (string.Equals(source, "measured", StringComparison.Ordinal) && PathValidation.IsMatch(reason));
        return boundary ? Boundary : null;
    }

    private static string Moniker(string version)
    {
        Version parsed = Version.Parse(version.Contains('.', StringComparison.Ordinal) ? version : version + ".0");
        string prefix = parsed.Major < 5 ? "netcoreapp" : "net";
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{parsed.Major}.{parsed.Minor}");
    }

    private static string Text(JsonElement row, string property) => row.GetProperty(property).GetString()!;

    /// <summary>The backfilled <paramref name="Lines"/> and the members of the rows left with a null <c>changedIn</c>.</summary>
    internal sealed record Result(IReadOnlyList<string> Lines, IReadOnlyList<string> Unplaced);
}
