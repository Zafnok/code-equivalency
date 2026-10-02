using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// The runtime-changes table (VERIFICATION-MODEL.md section 3; ADR 0008), loaded once from the
/// embedded <c>runtime-changes.json</c> resource. <see cref="TryMatch(CallIdentity, RuntimeInterval, out RuntimeChange)"/>
/// matches a call's <see cref="CallIdentity.Value"/> by prefix against the <see cref="Rows"/> whose
/// <see cref="RuntimeChange.ChangedIn"/> the pair's runtimes cross (ADR 0040 decision 2; tickets P2-054, P2-055); a member
/// listed in <c>equiv.config.json</c>'s <c>suppressRuntimeChanges</c> is excluded by the suppressing
/// overload, which the frontend's flagging uses (ticket M3-001), so a suppressed call is never flagged.
/// </summary>
public sealed class RuntimeChangeTable
{
    private const string ResourceName = "Equiv.Core.RuntimeChanges.runtime-changes.json";

    private static readonly Lazy<RuntimeChangeTable> Cached = new(LoadFromResource);

    /// <summary>The oldest runtime a compared project can target (ADR 0040): .NET Framework 4.0, older than every change point.</summary>
    private static readonly TargetRuntime Oldest = new(TargetRuntime.RuntimeFamily.NetFramework, new Version(4, 0));

    private RuntimeChangeTable(TargetRuntime coveredFrom, ImmutableArray<RuntimeChange> rows)
    {
        CoveredFrom = coveredFrom;
        Rows = rows;
        Coverage = new RuntimeInterval(Oldest, rows.Select(static row => row.ChangedIn).OfType<TargetRuntime>().Append(coveredFrom).Max()!);
    }

    /// <summary>The oldest .NET version whose changes the table lists; see <see cref="RuntimeInterval.UncoveredRange(TargetRuntime)"/>.</summary>
    public TargetRuntime CoveredFrom { get; }

    /// <summary>
    /// The interval that crosses every row: from .NET Framework to the newest runtime a row names. It stands for a pair
    /// whose runtimes are not known (ADR 0040 decision 1: such a pair crosses every change the table covers).
    /// </summary>
    public RuntimeInterval Coverage { get; }

    /// <summary>Every row, in file order.</summary>
    public ImmutableArray<RuntimeChange> Rows { get; }

    /// <summary>Reads and parses the embedded resource on first use; later calls return the same instance.</summary>
    public static RuntimeChangeTable Load() => Cached.Value;

    /// <summary>
    /// Matches <paramref name="identity"/>'s <see cref="CallIdentity.Value"/> by prefix against the rows that apply inside
    /// <paramref name="interval"/>: one whose <see cref="RuntimeChange.ChangedIn"/> the interval crosses, or, when that is
    /// unknown, any row once the runtimes differ.
    /// </summary>
    public bool TryMatch(CallIdentity identity, RuntimeInterval interval, out RuntimeChange match) =>
        TryMatch(identity, interval, [], out match);

    /// <summary>As the three-argument overload, but a row whose <see cref="RuntimeChange.Member"/> is in <paramref name="suppressed"/> never matches.</summary>
    public bool TryMatch(CallIdentity identity, RuntimeInterval interval, ImmutableArray<string> suppressed, out RuntimeChange match)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(interval);

        match = Rows.FirstOrDefault(row =>
            AppliesWithin(row, interval)
            && identity.Value.StartsWith(row.Member, StringComparison.Ordinal)
            && !suppressed.Contains(row.Member, StringComparer.Ordinal))!;
        return match is not null;
    }

    private static bool AppliesWithin(RuntimeChange row, RuntimeInterval interval) =>
        row.ChangedIn is { } changedIn ? interval.Crosses(changedIn) : !interval.IsEmpty;

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
    /// Parses a table in the embedded resource's format: an object with <c>coveredFrom</c> and <c>rows</c>. A row whose
    /// <c>source</c> is missing, or is not <c>curated</c>, <c>documented</c> or <c>measured</c> (ADR 0035), or whose
    /// <c>changedIn</c> is missing or is neither null nor a .NET Framework or .NET target framework moniker (ADR 0040),
    /// is rejected with <see cref="InvalidDataException"/>, as is a malformed <c>coveredFrom</c>.
    /// </summary>
    internal static RuntimeChangeTable Parse(Stream stream)
    {
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        JsonElement root = document.RootElement;
        string coveredFrom = root.GetProperty("coveredFrom").GetString()!;
        TargetRuntime coveredFromRuntime = TargetRuntime.Parse(coveredFrom)
            ?? throw new InvalidDataException($"runtime-changes coveredFrom '{coveredFrom}' is not a target framework moniker");

        ImmutableArray<RuntimeChange>.Builder builder = ImmutableArray.CreateBuilder<RuntimeChange>();
        foreach (JsonElement element in root.GetProperty("rows").EnumerateArray())
        {
            string member = element.GetProperty("member").GetString()!;
            builder.Add(new RuntimeChange(
                member,
                element.GetProperty("reason").GetString()!,
                new Uri(element.GetProperty("url").GetString()!, UriKind.Absolute),
                ParseSource(member, element))
            {
                Witness = ParseWitness(element),
                ChangedIn = ParseChangedIn(member, element),
            });
        }

        return new RuntimeChangeTable(coveredFromRuntime, builder.ToImmutable());
    }

    private static TargetRuntime? ParseChangedIn(string member, JsonElement element) =>
        !element.TryGetProperty("changedIn", out JsonElement value)
            ? throw new InvalidDataException($"runtime-changes row '{member}' has no changedIn")
            : value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when TargetRuntime.Parse(value.GetString()!) is { } runtime => runtime,
                _ => throw new InvalidDataException($"runtime-changes row '{member}' has malformed changedIn {value.GetRawText()}"),
            };

    private static RuntimeChangeWitness? ParseWitness(JsonElement element) =>
        element.TryGetProperty("witness", out JsonElement witness) ? BuildWitness(witness) : null;

    private static RuntimeChangeWitness BuildWitness(JsonElement witness) => new(
        [.. witness.GetProperty("input").EnumerateArray().Select(static v => v.GetRawText())],
        witness.GetProperty("culture").GetString()!,
        witness.GetProperty("legacy").GetRawText(),
        witness.GetProperty("modern").GetRawText());

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
