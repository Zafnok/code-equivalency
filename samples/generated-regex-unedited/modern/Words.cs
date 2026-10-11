using System.Text.RegularExpressions;

namespace Equiv.Samples.GeneratedRegexUnedited;

public static partial class Words
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    public static string Collapse(string text) => WhiteSpace().Replace(text, " ");
}
