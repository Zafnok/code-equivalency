using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Equiv.Core.Configuration;

/// <summary>
/// Parses and validates <c>equiv.config.json</c>. Walks a <see cref="JsonDocument"/> by hand instead
/// of <c>JsonSerializer.Deserialize</c> (no reflection, per CLAUDE.md) so every problem gets a
/// specific, helpful message rather than a generic deserialization failure.
/// </summary>
public static class EquivConfigLoader
{
    private static readonly FrozenSet<string> KnownProperties =
        new[] { "namespaceRenames", "typeRenames", "callIdentityRenames", "bound", "timeoutMs", "suppressRuntimeChanges" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Parses <paramref name="json"/> and validates it against the schema. Throws <see cref="EquivConfigParseException"/>
    /// when the text is not valid JSON at all; otherwise never throws, returning defaults plus a
    /// diagnostic for anything that does not fit the schema.
    /// </summary>
    public static EquivConfigResult Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = ParseDocument(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new EquivConfigResult(EquivConfig.Default, [Diagnostic(EquivConfigDiagnosticIds.InvalidRoot, string.Empty, "equiv.config.json must contain a JSON object")]);
        }

        ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<EquivConfigDiagnostic>();
        foreach (string propertyName in root.EnumerateObject().Where(property => !KnownProperties.Contains(property.Name)).Select(property => property.Name))
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.UnknownProperty, propertyName, $"unknown property \"{propertyName}\""));
        }

        RenameMap renames = new(
            ReadRenameMap(root, "namespaceRenames", diagnostics),
            ReadRenameMap(root, "typeRenames", diagnostics));
        ImmutableDictionary<string, string> callIdentityRenames = ReadRenameMap(root, "callIdentityRenames", diagnostics);
        int bound = ReadPositiveInt(root, "bound", EquivConfig.Default.Bound, EquivConfigDiagnosticIds.InvalidBound, diagnostics);
        int timeoutMs = ReadPositiveInt(root, "timeoutMs", EquivConfig.Default.TimeoutMs, EquivConfigDiagnosticIds.InvalidTimeout, diagnostics);
        ImmutableArray<string> suppressRuntimeChanges = ReadStringArray(root, "suppressRuntimeChanges", diagnostics);

        EquivConfig config = new(renames, callIdentityRenames, bound, timeoutMs) { SuppressRuntimeChanges = suppressRuntimeChanges };
        return new EquivConfigResult(config, diagnostics.ToImmutable());
    }

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new EquivConfigParseException($"equiv.config.json is not valid JSON: {exception.Message}", exception);
        }
    }

    private static ImmutableDictionary<string, string> ReadRenameMap(JsonElement root, string property, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRenameEntry, property, $"\"{property}\" must be an object of string to string"));
            return [];
        }

        ImmutableDictionary<string, string>.Builder map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in element.EnumerateObject())
        {
            string path = $"{property}/{entry.Name}";
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRenameEntry, path, "value must be a string"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRenameEntry, path, "key must not be empty"));
                continue;
            }

            string value = entry.Value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRenameEntry, path, "value must not be empty"));
                continue;
            }

            if (map.ContainsKey(entry.Name))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.DuplicateRenameEntry, path, $"duplicate key \"{entry.Name}\" (JSON keeps only the last one)"));
            }

            map[entry.Name] = value;
        }

        return map.ToImmutable();
    }

    private static ImmutableArray<string> ReadStringArray(JsonElement root, string property, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSuppressRuntimeChangesEntry, property, $"\"{property}\" must be an array of non-empty strings"));
            return [];
        }

        ImmutableArray<string>.Builder items = ImmutableArray.CreateBuilder<string>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string path = $"{property}/{index.ToString(CultureInfo.InvariantCulture)}";
            string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSuppressRuntimeChangesEntry, path, "value must be a non-empty string"));
            }
            else
            {
                items.Add(value);
            }

            index++;
        }

        return items.ToImmutable();
    }

    private static int ReadPositiveInt(JsonElement root, string property, int defaultValue, string diagnosticId, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            diagnostics.Add(Diagnostic(diagnosticId, property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        if (!element.TryGetInt32(out int value))
        {
            diagnostics.Add(Diagnostic(diagnosticId, property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        if (value <= 0)
        {
            diagnostics.Add(Diagnostic(diagnosticId, property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        return value;
    }

    private static EquivConfigDiagnostic Diagnostic(string id, string path, string message) => new(id, "/" + path, message);
}
