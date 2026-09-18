using System.Text.RegularExpressions;

namespace CheckCoverage;

internal static partial class ExcludeFromCoverageAuditor
{
    public static IReadOnlyList<CoverageExclusionViolation> AuditDirectory(string srcDirectory)
    {
        List<CoverageExclusionViolation> violations = [];

        foreach (string file in Directory.EnumerateFiles(srcDirectory, "*.cs", SearchOption.AllDirectories))
        {
            violations.AddRange(AuditSource(file, File.ReadAllText(file)));
        }

        return violations;
    }

    public static IReadOnlyList<CoverageExclusionViolation> AuditSource(string filePath, string sourceText)
    {
        List<CoverageExclusionViolation> violations = [];

        foreach (Match attribute in AttributeRegex().Matches(sourceText))
        {
            Match justification = JustificationRegex().Match(attribute.Value);
            int lineNumber = LineNumberAt(sourceText, attribute.Index);

            if (!justification.Success)
            {
                violations.Add(new CoverageExclusionViolation(filePath, lineNumber, "ExcludeFromCodeCoverage has no Justification."));
                continue;
            }

            string justificationText = justification.Groups["justification"].Value;
            if (!TicketIdRegex().IsMatch(justificationText))
            {
                violations.Add(new CoverageExclusionViolation(
                    filePath,
                    lineNumber,
                    $"Justification \"{justificationText}\" does not name a ticket id (e.g. M0-003)."));
            }
        }

        return violations;
    }

    private static int LineNumberAt(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    [GeneratedRegex(@"\[[^\]]*?ExcludeFromCodeCoverage[^\]]*?\]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"Justification\s*=\s*""(?<justification>[^""]*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JustificationRegex();

    [GeneratedRegex(@"\b[A-Z]\d+-\d{3}\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TicketIdRegex();
}