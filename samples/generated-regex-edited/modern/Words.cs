using System.Text.RegularExpressions;

namespace Equiv.Samples.GeneratedRegexEdited;

public static partial class Words
{
    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex WhiteSpace();

    public static string Collapse(string text) => WhiteSpace().Replace(text, " ");
}
