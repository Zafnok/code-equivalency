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
    /// <c>{"const": literal, "type": irType}</c>, whose literal is kept as its JSON text.
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
        return element.TryGetProperty("legacyType", out JsonElement legacyType)
            ? new ApiEquivalence(id, IsType: true, legacyType.GetString()!, element.GetProperty("modernType").GetString()!, [], reason, url)
            : new ApiEquivalence(
                id,
                IsType: false,
                element.GetProperty("legacy").GetString()!,
                element.GetProperty("modern").GetString()!,
                [.. element.GetProperty("arguments").EnumerateArray().Select(Argument)],
                reason,
                url);
    }

    private static ApiArgument Argument(JsonElement element)
    {
        if (element.TryGetProperty("const", out JsonElement constant))
        {
            return new ApiArgument(Source: null, ConstantType: element.GetProperty("type").GetString()!, Constant: constant.GetRawText());
        }

        // Deliberate non-short-circuit '&': when "unwrap" is absent, the default element's kind is Undefined, not True.
        bool unwrap = element.TryGetProperty("unwrap", out JsonElement flag) & flag.ValueKind == JsonValueKind.True; // NOSONAR
        string? convertTo = element.TryGetProperty("convertTo", out JsonElement type) ? type.GetString() : null;
        return new ApiArgument(element.GetProperty("arg").GetInt32(), unwrap, convertTo);
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
