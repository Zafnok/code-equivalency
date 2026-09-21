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

    private readonly ImmutableArray<RuntimeChange> rows;

    private RuntimeChangeTable(ImmutableArray<RuntimeChange> rows)
    {
        this.rows = rows;
    }

    /// <summary>Every row, in file order.</summary>
    public ImmutableArray<RuntimeChange> Rows => rows;

    /// <summary>Reads and parses the embedded resource on first use; later calls return the same instance.</summary>
    public static RuntimeChangeTable Load() => Cached.Value;

    /// <summary>Matches <paramref name="identity"/>'s <see cref="CallIdentity.Value"/> by prefix against every row.</summary>
    public bool TryMatch(CallIdentity identity, out RuntimeChange match) => TryMatch(identity, [], out match);

    /// <summary>As the two-argument overload, but a row whose <see cref="RuntimeChange.Member"/> is in <paramref name="suppressed"/> never matches.</summary>
    public bool TryMatch(CallIdentity identity, ImmutableArray<string> suppressed, out RuntimeChange match)
    {
        ArgumentNullException.ThrowIfNull(identity);

        foreach (RuntimeChange row in rows)
        {
            if (identity.Value.StartsWith(row.Member, StringComparison.Ordinal) && !suppressed.Contains(row.Member, StringComparer.Ordinal))
            {
                match = row;
                return true;
            }
        }

        match = null!;
        return false;
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
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        ImmutableArray<RuntimeChange>.Builder builder = ImmutableArray.CreateBuilder<RuntimeChange>();
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            builder.Add(new RuntimeChange(
                element.GetProperty("member").GetString()!,
                element.GetProperty("reason").GetString()!,
                new Uri(element.GetProperty("url").GetString()!, UriKind.Absolute)));
        }

        return new RuntimeChangeTable(builder.ToImmutable());
    }
}
