using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// The runtime-changes table (VERIFICATION-MODEL.md section 3; ADR 0008), loaded once from the
/// embedded <c>runtime-changes.json</c> resource. <see cref="TryMatch(CallIdentity, out RuntimeChange)"/>
/// matches a call's <see cref="CallIdentity.Value"/> by prefix against <see cref="Rows"/>; a member
/// listed in <c>equiv.config.json</c>'s <c>suppressRuntimeChanges</c> is excluded by the suppressing
/// overload, which the frontend's flagging uses (ticket M3-001), so a suppressed call is never flagged.
/// </summary>
public sealed class RuntimeChangeTable
{
    private const string ResourceName = "Equiv.Core.RuntimeChanges.runtime-changes.json";

    private static readonly Lazy<RuntimeChangeTable> Cached = new(LoadFromResource);

    private RuntimeChangeTable(ImmutableArray<RuntimeChange> rows)
    {
        Rows = rows;
    }

    /// <summary>Every row, in file order.</summary>
    public ImmutableArray<RuntimeChange> Rows { get; }

    /// <summary>Reads and parses the embedded resource on first use; later calls return the same instance.</summary>
    public static RuntimeChangeTable Load() => Cached.Value;

    /// <summary>Matches <paramref name="identity"/>'s <see cref="CallIdentity.Value"/> by prefix against every row.</summary>
    public bool TryMatch(CallIdentity identity, out RuntimeChange match) => TryMatch(identity, [], out match);

    /// <summary>As the two-argument overload, but a row whose <see cref="RuntimeChange.Member"/> is in <paramref name="suppressed"/> never matches.</summary>
    public bool TryMatch(CallIdentity identity, ImmutableArray<string> suppressed, out RuntimeChange match)
    {
        ArgumentNullException.ThrowIfNull(identity);

        match = Rows.FirstOrDefault(row =>
            identity.Value.StartsWith(row.Member, StringComparison.Ordinal) && !suppressed.Contains(row.Member, StringComparer.Ordinal))!;
        return match is not null;
    }

    /// <summary>
    /// True when <paramref name="member"/> parses as an identity prefix: a non-empty qualified type,
    /// then exactly one <c>"::"</c>, matching the shape <see cref="Equiv.Core.Matching.ProcedureIdentityNormalizer.Member"/>
    /// produces (<c>Namespace.Type::Member(...)</c> or, for a whole-type row, <c>Namespace.Type::</c>).
    /// </summary>
    internal static bool IsIdentityPrefix(string member)
    {
        string[] parts = member.Split("::");
        return parts.Length == 2 && parts[0].Length > 0;
    }

    private static RuntimeChangeTable LoadFromResource()
    {
        Assembly assembly = typeof(RuntimeChangeTable).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"embedded resource '{ResourceName}' not found");
        return Parse(stream);
    }

    /// <summary>
    /// Parses a table in the embedded resource's format. A row whose <c>source</c> is missing, or is
    /// not <c>curated</c>, <c>documented</c> or <c>measured</c> (ADR 0035), is rejected with
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    internal static RuntimeChangeTable Parse(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        ImmutableArray<RuntimeChange>.Builder builder = ImmutableArray.CreateBuilder<RuntimeChange>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            string member = element.GetProperty("member").GetString()!;
            builder.Add(new RuntimeChange(
                member,
                element.GetProperty("reason").GetString()!,
                new Uri(element.GetProperty("url").GetString()!, UriKind.Absolute),
                ParseSource(member, element)));
        }

        return new RuntimeChangeTable(builder.ToImmutable());
    }

    private static RuntimeChangeSource ParseSource(string member, JsonElement element)
    {
        string? source = element.TryGetProperty("source", out JsonElement value) ? value.GetString() : null;
        return source switch
        {
            "curated" => RuntimeChangeSource.Curated,
            "documented" => RuntimeChangeSource.Documented,
            "measured" => RuntimeChangeSource.Measured,
            null => throw new InvalidDataException($"runtime-changes row '{member}' has no source"),
            _ => throw new InvalidDataException($"runtime-changes row '{member}' has unknown source '{source}'"),
        };
    }
}
