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
        new[] { "namespaceRenames", "typeRenames", "callIdentityRenames", "bound", "timeoutMs", "resourceLimit", "jobs", "suppressRuntimeChanges", "suppressApiEquivalences", "runtimes", "solvers", "mode", "escalation" }.ToFrozenSet(StringComparer.Ordinal);

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
        int resourceLimit = ReadPositiveInt(root, "resourceLimit", EquivConfig.DefaultResourceLimit, EquivConfigDiagnosticIds.InvalidResourceLimit, diagnostics);
        int jobs = ReadPositiveInt(root, "jobs", EquivConfig.Default.Jobs, EquivConfigDiagnosticIds.InvalidJobs, diagnostics);
        ImmutableArray<string> suppressRuntimeChanges = ReadStringArray(root, "suppressRuntimeChanges", EquivConfigDiagnosticIds.InvalidSuppressRuntimeChangesEntry, diagnostics);
        ImmutableArray<string> suppressApiEquivalences = ReadStringArray(root, "suppressApiEquivalences", EquivConfigDiagnosticIds.InvalidSuppressApiEquivalencesEntry, diagnostics);
        (TargetRuntime? legacyRuntime, TargetRuntime? modernRuntime) = ReadRuntimes(root, diagnostics);

        EquivConfig config = new(renames, callIdentityRenames, bound, timeoutMs)
        {
            SuppressRuntimeChanges = suppressRuntimeChanges,
            SuppressApiEquivalences = suppressApiEquivalences,
            LegacyRuntime = legacyRuntime,
            ModernRuntime = modernRuntime,
            ResourceLimit = resourceLimit,
            Jobs = jobs,
            Cvc5Path = ReadCvc5Path(root, diagnostics),
            Mode = ReadMode(root, diagnostics),
            Escalation = ReadEscalation(root, diagnostics),
        };
        config = config
            .WithExplicit(EquivConfig.BoundSetting, root.TryGetProperty(EquivConfig.BoundSetting, out _))
            .WithExplicit(EquivConfig.ResourceLimitSetting, root.TryGetProperty(EquivConfig.ResourceLimitSetting, out _))
            .WithExplicit(EquivConfig.TimeoutSetting, root.TryGetProperty(EquivConfig.TimeoutSetting, out _))
            .WithExplicit(EquivConfig.EscalationSetting, root.TryGetProperty(EquivConfig.EscalationSetting, out _));
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

    private static ImmutableArray<string> ReadStringArray(JsonElement root, string property, string diagnosticId, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(Diagnostic(diagnosticId, property, $"\"{property}\" must be an array of non-empty strings"));
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
                diagnostics.Add(Diagnostic(diagnosticId, path, "value must be a non-empty string"));
            }
            else
            {
                items.Add(value);
            }

            index++;
        }

        return items.ToImmutable();
    }

    /// <summary>
    /// <c>"runtimes": { "legacy": "&lt;tfm&gt;", "modern": "&lt;tfm&gt;" }</c> (ADR 0040; ticket P2-053). Each value is a
    /// .NET Framework or .NET (Core) moniker or short name that <see cref="TargetRuntime.Parse"/> accepts; either key may be absent.
    /// </summary>
    private static (TargetRuntime? Legacy, TargetRuntime? Modern) ReadRuntimes(JsonElement root, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        const string Property = "runtimes";
        if (!root.TryGetProperty(Property, out JsonElement element))
        {
            return (null, null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRuntimes, Property, $"\"{Property}\" must be an object with \"legacy\" and/or \"modern\""));
            return (null, null);
        }

        TargetRuntime? legacy = null;
        TargetRuntime? modern = null;
        foreach (JsonProperty entry in element.EnumerateObject())
        {
            string path = $"{Property}/{entry.Name}";
            if (entry.Name is not ("legacy" or "modern"))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRuntimes, path, $"unknown side \"{entry.Name}\" (expected \"legacy\" or \"modern\")"));
                continue;
            }

            TargetRuntime? runtime = entry.Value.ValueKind == JsonValueKind.String ? TargetRuntime.Parse(entry.Value.GetString()!) : null;
            if (runtime is null)
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidRuntimes, path, "value must be a .NET Framework or .NET target framework, such as \"net48\" or \"net8.0\""));
            }
            else if (string.Equals(entry.Name, "legacy", StringComparison.Ordinal))
            {
                legacy = runtime;
            }
            else
            {
                modern = runtime;
            }
        }

        return (legacy, modern);
    }

    /// <summary>
    /// <c>"solvers": { "cvc5": { "path": "&lt;executable&gt;" } }</c> (ADR 0050 decision 6; ticket P1-033). Anything else
    /// under <c>solvers</c> is a diagnostic and configures no solver.
    /// </summary>
    private static string? ReadCvc5Path(JsonElement root, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        const string Property = "solvers";
        const string Solver = "cvc5";
        const string Path = "path";
        if (!root.TryGetProperty(Property, out JsonElement solvers))
        {
            return null;
        }

        if (solvers.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSolvers, Property, $"\"{Property}\" must be an object with \"{Solver}\""));
            return null;
        }

        string? path = null;
        foreach (JsonProperty solver in solvers.EnumerateObject())
        {
            string at = $"{Property}/{solver.Name}";
            if (!string.Equals(solver.Name, Solver, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSolvers, at, $"unknown solver \"{solver.Name}\" (expected \"{Solver}\")"));
            }
            else if (solver.Value.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSolvers, at, $"\"{Solver}\" must be an object with \"{Path}\""));
            }
            else
            {
                path = ReadSolverPath(solver.Value, at, diagnostics);
            }
        }

        return path;
    }

    private static string? ReadSolverPath(JsonElement solver, string at, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        const string Path = "path";
        string? path = null;
        foreach (JsonProperty entry in solver.EnumerateObject())
        {
            string? value = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
            if (!string.Equals(entry.Name, Path, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSolvers, $"{at}/{entry.Name}", $"unknown property \"{entry.Name}\" (expected \"{Path}\")"));
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidSolvers, $"{at}/{Path}", "value must be a non-empty string"));
            }
            else
            {
                path = value;
            }
        }

        return path;
    }

    /// <summary>
    /// <c>"mode": "thorough" | "quick"</c> (ADR 0049 decision 1; ticket P1-032), or null for any other name. The names are
    /// the ones <c>--mode</c> takes.
    /// </summary>
    public static CompareMode? ParseMode(string? name) => name switch
    {
        "thorough" => CompareMode.Thorough,
        "quick" => CompareMode.Quick,
        _ => null,
    };

    private static CompareMode ReadMode(JsonElement root, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        const string Property = "mode";
        if (!root.TryGetProperty(Property, out JsonElement element))
        {
            return EquivConfig.Default.Mode;
        }

        CompareMode? mode = element.ValueKind == JsonValueKind.String ? ParseMode(element.GetString()) : null;
        if (mode is null)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidMode, Property, $"\"{Property}\" must be \"thorough\" or \"quick\""));
        }

        return mode ?? EquivConfig.Default.Mode;
    }

    /// <summary>
    /// <c>"escalation": { "bound": n, "resourceLimit": n, "timeoutMs": n }</c> (ADR 0049 decision 4; ticket P1-032). A key
    /// that is absent keeps ADR 0049's value; one that is not a positive integer, or not one of the three, is a diagnostic.
    /// </summary>
    private static Escalation ReadEscalation(JsonElement root, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics)
    {
        const string Property = "escalation";
        Escalation defaults = Escalation.Default;
        if (!root.TryGetProperty(Property, out JsonElement element))
        {
            return defaults;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidEscalation, Property, $"\"{Property}\" must be an object with \"bound\", \"resourceLimit\" and/or \"timeoutMs\""));
            return defaults;
        }

        foreach (string name in element.EnumerateObject().Select(static entry => entry.Name).Where(static name => name is not (EquivConfig.BoundSetting or EquivConfig.ResourceLimitSetting or EquivConfig.TimeoutSetting)))
        {
            diagnostics.Add(Diagnostic(EquivConfigDiagnosticIds.InvalidEscalation, $"{Property}/{name}", $"unknown property \"{name}\""));
        }

        return new Escalation(
            ReadPositiveInt(element, EquivConfig.BoundSetting, defaults.Bound, EquivConfigDiagnosticIds.InvalidEscalation, diagnostics, Property + "/"),
            ReadPositiveInt(element, EquivConfig.ResourceLimitSetting, defaults.ResourceLimit, EquivConfigDiagnosticIds.InvalidEscalation, diagnostics, Property + "/"),
            ReadPositiveInt(element, EquivConfig.TimeoutSetting, defaults.TimeoutMs, EquivConfigDiagnosticIds.InvalidEscalation, diagnostics, Property + "/"));
    }

    private static int ReadPositiveInt(JsonElement root, string property, int defaultValue, string diagnosticId, ImmutableArray<EquivConfigDiagnostic>.Builder diagnostics, string within = "")
    {
        if (!root.TryGetProperty(property, out JsonElement element))
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            diagnostics.Add(Diagnostic(diagnosticId, within + property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        if (!element.TryGetInt32(out int value))
        {
            diagnostics.Add(Diagnostic(diagnosticId, within + property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        if (value <= 0)
        {
            diagnostics.Add(Diagnostic(diagnosticId, within + property, $"\"{property}\" must be a positive integer"));
            return defaultValue;
        }

        return value;
    }

    private static EquivConfigDiagnostic Diagnostic(string id, string path, string message) => new(id, "/" + path, message);
}
