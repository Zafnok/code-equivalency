using System.Globalization;

using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// The driver's wire format (ticket M3-032). A case goes in as <c>[culture,arg0,arg1,...]</c>; its outcome comes back as
/// <c>["Kind",canonical]</c>, where the kind is an <see cref="OutcomeKind"/> name.
/// </summary>
internal static class OutcomeLine
{
    /// <summary>The canonical form of a case that got no answer because its driver went over the memory limit or exited.</summary>
    public const string NoAnswer = "\"no answer\"";

    private static readonly IReadOnlyList<OutcomeKind> Kinds =
        [OutcomeKind.Returned, OutcomeKind.Threw, OutcomeKind.NotComparable, OutcomeKind.NotConstructible];

    /// <summary>A kind's name on the wire and in the report, spelled out so no enum formatting is involved.</summary>
    public static string Name(OutcomeKind kind) => kind switch
    {
        OutcomeKind.Returned => "Returned",
        OutcomeKind.Threw => "Threw",
        OutcomeKind.NotComparable => "NotComparable",
        _ => "NotConstructible",
    };

    /// <summary>The canonical form of a case whose driver gave no answer within <paramref name="timeout"/> (ticket P2-039).</summary>
    public static string TimedOut(TimeSpan timeout) =>
        JsonText.String(string.Create(CultureInfo.InvariantCulture, $"no answer within {timeout.TotalSeconds} s"));

    public static string Case(string culture, ExecutionInput input) =>
        $"[{string.Join(',', [JsonText.String(culture), .. input.Arguments])}]";

    /// <summary>The kind and canonical text of an answer; a line in no known shape is <see cref="OutcomeKind.NotComparable"/>.</summary>
    public static (OutcomeKind Kind, string Canonical) Parse(string line)
    {
        foreach (OutcomeKind kind in Kinds)
        {
            string prefix = $"[\"{Name(kind)}\",";
            if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(']'))
            {
                return (kind, line[prefix.Length..^1]);
            }
        }

        return (OutcomeKind.NotComparable, JsonText.String("malformed: " + line));
    }
}
