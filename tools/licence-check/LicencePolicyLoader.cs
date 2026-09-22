using System.Text.Json;

namespace LicenceCheck;

/// <summary>
/// Parses <c>policy.json</c> by walking <see cref="JsonDocument"/> by hand instead of
/// <c>JsonSerializer.Deserialize</c> (no reflection, per CLAUDE.md), same convention as
/// <c>EquivConfigLoader</c>. Unlike that loader this one throws on a bad entry rather than
/// collecting diagnostics: an exception with no reason is a hard error that stops the run
/// before any package is evaluated (ticket M0-010, AC2).
/// </summary>
internal static class LicencePolicyLoader
{
    public static LicencePolicy Load(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<string> allowedLicenses = [];
        if (root.TryGetProperty("allowedLicenses", out JsonElement allowedElement))
        {
            foreach (JsonElement item in allowedElement.EnumerateArray())
            {
                allowedLicenses.Add(RequireString(item, "allowedLicenses entry"));
            }
        }

        List<LicenceException> exceptions = [];
        if (root.TryGetProperty("exceptions", out JsonElement exceptionsElement))
        {
            foreach (JsonElement item in exceptionsElement.EnumerateArray())
            {
                string packageId = RequireProperty(item, "packageId");
                string licence = RequireProperty(item, "licence");
                string reason = item.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                    ? reasonElement.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new LicenceCheckException($"policy.json: the exception for package '{packageId}' has no reason. Every exception must state why it is permitted.");
                }

                exceptions.Add(new LicenceException(packageId, licence, reason));
            }
        }

        return new LicencePolicy(allowedLicenses, exceptions);
    }

    private static string RequireProperty(JsonElement element, string property)
    {
        return !element.TryGetProperty(property, out JsonElement value)
            ? throw new LicenceCheckException($"policy.json: an exception entry is missing required property '{property}'.")
            : RequireString(value, property);
    }

    private static string RequireString(JsonElement element, string what)
    {
        return element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString())
            ? throw new LicenceCheckException($"policy.json: {what} must be a non-empty string.")
            : element.GetString()!;
    }
}
