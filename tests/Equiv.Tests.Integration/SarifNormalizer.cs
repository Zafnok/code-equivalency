using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equiv.Tests.Integration;

/// <summary>
/// Makes an <c>equiv compare</c> SARIF log comparable across machines and runs (ticket M3-003 criterion 3):
/// every string that carries <paramref name="sampleDir"/> as an absolute prefix (a <c>physicalLocation</c>'s
/// <c>artifactLocation.uri</c>) becomes sample-relative (<c>legacy/Foo.cs</c>, <c>modern/Foo.cs</c>), and
/// <c>invocations[].startTimeUtc</c>/<c>endTimeUtc</c> and the tool driver's <c>version</c> are removed, since
/// none of the three is deterministic across machines or SDK builds. Only string <em>values</em> are rewritten
/// (never property names), so this cannot corrupt the JSON structure.
/// </summary>
internal static class SarifNormalizer
{
    public static string Normalize(string json, string sampleDir)
    {
        string prefixSlash = sampleDir.Replace('\\', '/') + "/";
        string prefixBackslash = sampleDir.Replace('/', '\\') + "\\";

        JObject root = JObject.Parse(json);
        foreach (JObject run in (root["runs"] as JArray)?.OfType<JObject>() ?? [])
        {
            if (run.SelectToken("tool.driver") is JObject driver)
            {
                driver.Remove("version");
            }

            foreach (JObject invocation in (run["invocations"] as JArray)?.OfType<JObject>() ?? [])
            {
                invocation.Remove("startTimeUtc");
                invocation.Remove("endTimeUtc");
            }
        }

        foreach (JValue value in root.Descendants().OfType<JValue>().Where(static v => v.Type == JTokenType.String).ToList())
        {
            string text = (string)value.Value!;
            string relative = text.Replace(prefixSlash, string.Empty, StringComparison.Ordinal).Replace(prefixBackslash, string.Empty, StringComparison.Ordinal);
            if (!string.Equals(relative, text, StringComparison.Ordinal))
            {
                value.Value = relative;
            }
        }

        return root.ToString(Formatting.Indented);
    }
}
