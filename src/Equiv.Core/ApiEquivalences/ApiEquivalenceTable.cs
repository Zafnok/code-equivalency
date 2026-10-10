using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// The API-equivalence catalogue (VERIFICATION-MODEL.md section 3; ADR 0020; ticket M3-009), loaded once from the
/// embedded <c>api-equivalences.json</c> resource. A frontend applies <see cref="Enabled"/>'s entries while lowering the
/// legacy side only; an entry whose <see cref="ApiEquivalence.Id"/> starts with a prefix listed in
/// <c>equiv.config.json</c>'s <c>suppressApiEquivalences</c> is left out.
/// </summary>
public sealed class ApiEquivalenceTable
{
    private const string ResourceName = "Equiv.Core.ApiEquivalences.api-equivalences.json";

    private static readonly Lazy<ApiEquivalenceTable> Cached = new(LoadFromResource);

    private ApiEquivalenceTable(ImmutableArray<ApiEquivalence> entries)
    {
        Entries = entries;
    }

    /// <summary>Every entry, in file order.</summary>
    public ImmutableArray<ApiEquivalence> Entries { get; }

    /// <summary>Reads and parses the embedded resource on first use; later calls return the same instance.</summary>
    public static ApiEquivalenceTable Load() => Cached.Value;

    /// <summary>The entries whose id starts with none of <paramref name="suppressed"/>, in file order.</summary>
    public ImmutableArray<ApiEquivalence> Enabled(ImmutableArray<string> suppressed) =>
        [.. Entries.Where(entry => !suppressed.Any(prefix => entry.Id.StartsWith(prefix, StringComparison.Ordinal)))];

    /// <summary>
    /// Parses the catalogue's JSON: an array of member entries (<c>id</c>, <c>legacy</c>, <c>modern</c>, <c>arguments</c>,
    /// <c>reason</c>, <c>url</c>) and type entries (<c>id</c>, <c>legacyType</c>, <c>modernType</c>, <c>reason</c>,
    /// <c>url</c>). Each argument is <c>{"arg": n}</c>, optionally with <c>"unwrap": true</c> or <c>"convertTo"</c>, or
    /// <c>{"const": literal, "type": irType}</c>, whose literal is kept as its JSON text, or <c>{"rest": n}</c>. An <c>arg</c> item may carry
    /// <c>"integer": {"bits": n, "min": a, "max": b}</c>, and a member entry <c>"addedIn"</c>, a target framework moniker;
    /// one that names no runtime is rejected with <see cref="InvalidDataException"/> (ticket P2-142). An argument may
    /// also be <c>{"typeArgument": n}</c>, an <c>arg</c> item may carry <c>"ofTypeArgument": true</c>, and a member entry
    /// <c>"returnsTypeArgumentArray": true</c>, which without a <c>typeArgument</c> item is rejected likewise (ticket P2-117).
    /// </summary>
    internal static ApiEquivalenceTable Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        return new ApiEquivalenceTable([.. document.RootElement.EnumerateArray().Select(Entry)]);
    }

    private static ApiEquivalence Entry(JsonElement element)
    {
        string id = element.GetProperty("id").GetString()!;
        string reason = element.GetProperty("reason").GetString()!;
        Uri url = new(element.GetProperty("url").GetString()!, UriKind.Absolute);
        if (element.TryGetProperty("legacyType", out JsonElement legacyType))
        {
            return new ApiEquivalence(id, IsType: true, legacyType.GetString()!, element.GetProperty("modernType").GetString()!, [], reason, url);
        }

        ApiEquivalence member = new(
            id,
            IsType: false,
            element.GetProperty("legacy").GetString()!,
            element.GetProperty("modern").GetString()!,
            [.. element.GetProperty("arguments").EnumerateArray().Select(Argument)],
            reason,
            url)
        {
            AddedIn = element.TryGetProperty("addedIn", out JsonElement added) ? Runtime(id, added.GetString()!) : null,
            ReturnsTypeArgumentArray = Flag(element, "returnsTypeArgumentArray"),
        };
        return member.ReturnsTypeArgumentArray && !member.Arguments.Any(static a => a.TypeArgument)
            ? throw new InvalidDataException($"api-equivalences entry '{id}' returns an array of a type argument it does not have")
            : member;
    }

    /// <summary>
    /// Whether <paramref name="element"/> has the property <paramref name="name"/> with the value <c>true</c>. The '&amp;'
    /// is deliberately not short-circuit: when the property is absent, the default element's kind is Undefined, not True.
    /// </summary>
    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement flag) & flag.ValueKind == JsonValueKind.True; // NOSONAR

    private static TargetRuntime Runtime(string id, string text) =>
        TargetRuntime.Parse(text) ?? throw new InvalidDataException($"api-equivalences entry '{id}' has malformed addedIn '{text}'");

    private static ApiIntegerRange Range(JsonElement element) =>
        new(element.GetProperty("bits").GetInt32(), element.GetProperty("min").GetInt64(), element.GetProperty("max").GetInt64());

    private static ApiArgument Argument(JsonElement element)
    {
        if (element.TryGetProperty("const", out JsonElement constant))
        {
            return new ApiArgument(Source: null, ConstantType: element.GetProperty("type").GetString()!, Constant: constant.GetRawText());
        }

        if (element.TryGetProperty("rest", out JsonElement rest))
        {
            return new ApiArgument(rest.GetInt32(), Rest: true);
        }

        if (element.TryGetProperty("typeArgument", out JsonElement typeArgument))
        {
            return new ApiArgument(typeArgument.GetInt32()) { TypeArgument = true };
        }

        string? convertTo = element.TryGetProperty("convertTo", out JsonElement type) ? type.GetString() : null;
        return new ApiArgument(element.GetProperty("arg").GetInt32(), Flag(element, "unwrap"), convertTo)
        {
            Range = element.TryGetProperty("integer", out JsonElement range) ? Range(range) : null,
            OfTypeArgument = Flag(element, "ofTypeArgument"),
        };
    }

    private static ApiEquivalenceTable LoadFromResource()
    {
        Assembly assembly = typeof(ApiEquivalenceTable).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' not found");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }
}
