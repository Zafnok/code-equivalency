using System.Globalization;
using System.Xml.Linq;

namespace CheckCoverage;

// Each test project's cobertura report only exercises part of a shared assembly, so a
// line/branch counts as covered here if any report shows it covered.
internal static class CoberturaCoverageReader
{
    public static IReadOnlyDictionary<string, AssemblyCoverage> Merge(IEnumerable<string> coberturaXmlDocuments)
    {
        ArgumentNullException.ThrowIfNull(coberturaXmlDocuments);

        Dictionary<string, Dictionary<LineKey, LineAccumulator>> byAssembly = new(StringComparer.Ordinal);

        foreach (string xml in coberturaXmlDocuments)
        {
            MergeDocument(byAssembly, XDocument.Parse(xml));
        }

        Dictionary<string, AssemblyCoverage> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Dictionary<LineKey, LineAccumulator>> assembly in byAssembly)
        {
            int linesValid = assembly.Value.Count;
            int linesCovered = 0;
            int branchesValid = 0;
            int branchesCovered = 0;

            foreach (LineAccumulator line in assembly.Value.Values)
            {
                if (line.MaxHits > 0)
                {
                    linesCovered++;
                }

                branchesValid += line.Conditions.Count;
                branchesCovered += line.Conditions.Values.Count(static covered => covered);
            }

            result[assembly.Key] = new AssemblyCoverage(assembly.Key, linesValid, linesCovered, branchesValid, branchesCovered);
        }

        return result;
    }

    private static void MergeDocument(Dictionary<string, Dictionary<LineKey, LineAccumulator>> byAssembly, XDocument document)
    {
        // coverlet writes filenames relative to the report's common source root(s), which differ
        // per test project (".../src/" vs ".../src/Equiv.Core/Ir/"), so the same line appears under
        // different relative names. Anchor each filename to its <source> before merging.
        string[] sources = [.. document.Descendants("source").Select(static s => Normalize(s.Value).TrimEnd('/') + "/")];

        foreach (XElement package in document.Descendants("package"))
        {
            string assemblyName = (string?)package.Attribute("name") ?? string.Empty;

            if (!byAssembly.TryGetValue(assemblyName, out Dictionary<LineKey, LineAccumulator>? assemblyLines))
            {
                assemblyLines = [];
                byAssembly[assemblyName] = assemblyLines;
            }

            foreach (XElement classElement in package.Descendants("class"))
            {
                MergeClass(assemblyLines, classElement, sources);
            }
        }
    }

    private static void MergeClass(Dictionary<LineKey, LineAccumulator> assemblyLines, XElement classElement, string[] sources)
    {
        string className = (string?)classElement.Attribute("name") ?? string.Empty;
        string filename = Anchor(Normalize((string?)classElement.Attribute("filename") ?? string.Empty), sources);

        XElement? linesElement = classElement.Element("lines");
        if (linesElement is null)
        {
            return;
        }

        foreach (XElement lineElement in linesElement.Elements("line"))
        {
            LineKey key = new(className, filename, (int?)lineElement.Attribute("number") ?? 0);

            if (!assemblyLines.TryGetValue(key, out LineAccumulator? accumulator))
            {
                accumulator = new LineAccumulator();
                assemblyLines[key] = accumulator;
            }

            accumulator.MaxHits = Math.Max(accumulator.MaxHits, (long?)lineElement.Attribute("hits") ?? 0L);

            XElement? conditionsElement = lineElement.Element("conditions");
            if (conditionsElement is null)
            {
                continue;
            }

            foreach (XElement conditionElement in conditionsElement.Elements("condition"))
            {
                int conditionNumber = (int?)conditionElement.Attribute("number") ?? 0;
                bool covered = ParseCoveragePercentage((string?)conditionElement.Attribute("coverage") ?? "0%") > 0;

                accumulator.Conditions.TryGetValue(conditionNumber, out bool alreadyCovered);
                accumulator.Conditions[conditionNumber] = alreadyCovered || covered;
            }
        }
    }

    /// <summary>
    /// With one source the filename is relative to it. With several (e.g. third-party packages
    /// whose embedded sources live under "/_/"), it is relative to the one where the file exists.
    /// </summary>
    private static string Anchor(string filename, string[] sources) => sources.Length switch
    {
        0 => filename,
        1 => sources[0] + filename,
        _ => sources.Select(source => source + filename).FirstOrDefault(File.Exists) ?? filename,
    };

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static double ParseCoveragePercentage(string coverage)
    {
        string trimmed = coverage.TrimEnd('%');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0d;
    }

    private readonly record struct LineKey(string ClassName, string Filename, int Number);

    private sealed class LineAccumulator
    {
        public long MaxHits { get; set; }

        public Dictionary<int, bool> Conditions { get; } = [];
    }
}
