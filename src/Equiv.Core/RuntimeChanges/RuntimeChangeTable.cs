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

    private RuntimeChangeTable(TargetRuntime coveredFrom, ImmutableArray<RuntimeChange> rows, ValidPaths validPaths)
    {
        CoveredFrom = coveredFrom;
        Rows = rows;
        this.validPaths = validPaths;
        Coverage = new RuntimeInterval(Oldest, rows.Select(static row => row.ChangedIn).OfType<TargetRuntime>().Append(coveredFrom).Max()!);
    }

    private readonly ValidPaths validPaths;

    /// <summary>The oldest .NET version whose changes the table lists; see <see cref="RuntimeInterval.UncoveredRange(TargetRuntime)"/>.</summary>
    public TargetRuntime CoveredFrom { get; }

    /// <summary>
    /// The interval that crosses every row: from .NET Framework to the newest runtime a row names. It stands for a pair
    /// whose runtimes are not known (ADR 0040 decision 1: such a pair crosses every change the table covers).
    /// </summary>
    public RuntimeInterval Coverage { get; }

    /// <summary>Every row, in file order.</summary>
    public ImmutableArray<RuntimeChange> Rows { get; }

    /// <summary>The <see cref="RuntimeChange.Member"/> of every row that <see cref="RuntimeChange.Requires"/> <paramref name="precondition"/>.</summary>
    public ImmutableArray<string> MembersRequiring(RowPrecondition precondition) =>
        [.. Rows.Where(row => row.Requires == precondition).Select(static row => row.Member).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Whether the result of a call to <paramref name="member"/> with <paramref name="arguments"/> is a path .NET Framework's
    /// validation accepts (ticket P2-074): the member is one of the table's valid-path sources or validators (ticket
    /// P2-188), whatever its arguments; one of its transforms, and the call's arguments cannot reach
    /// <see cref="RuntimeChangePrecondition.InvalidPath"/>; or one of its prefixes, called on a valid path with the constant
    /// 0 as its <c>startIndex</c> (ticket P2-188).
    /// </summary>
    public bool YieldsValidPath(CallIdentity member, ImmutableArray<CallArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Lists(validPaths.Sources, member)
            || Lists(validPaths.Validators, member)
            || (Lists(validPaths.Prefixes, member) && IsStartOfValidPath(arguments))
            || (Lists(validPaths.Transforms, member) && !RuntimeChangeReach.CanReach(RuntimeChangePrecondition.InvalidPath, arguments));
    }

    /// <summary>The members that return their argument only once .NET Framework's validation has accepted it (ticket P2-188).</summary>
    internal ImmutableArray<string> PathValidators => validPaths.Validators;

    /// <summary>A call on a valid path whose <c>startIndex</c> is the constant 0, so it returns the path's start.</summary>
    private static bool IsStartOfValidPath(ImmutableArray<CallArgument> arguments) =>
        arguments.Any(static a => string.Equals(a.Parameter, CallArgument.Receiver, StringComparison.Ordinal) && a.ValidPath)
        && arguments.Any(static a => string.Equals(a.Parameter, "startIndex", StringComparison.Ordinal) && a.IsConstant && a.Value is 0);

    /// <summary>Whether <paramref name="member"/> is one of the table's validating guards: it returns true only for a path .NET Framework's validation accepts (ticket P2-074).</summary>
    public bool ValidatesPath(CallIdentity member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return Lists(validPaths.Guards, member);
    }

    private static bool Lists(ImmutableArray<string> members, CallIdentity member) =>
        members.Any(listed => member.Value.StartsWith(listed, StringComparison.Ordinal));

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
    public bool TryMatch(CallIdentity identity, RuntimeInterval interval, ImmutableArray<string> suppressed, out RuntimeChange match) =>
        TryMatch(identity, interval, suppressed, ordinalComparison: false, [], out match);

    /// <summary>
    /// As the four-argument overload, but when <paramref name="ordinalComparison"/> is true, the call passes
    /// <c>StringComparison.Ordinal</c> or <c>OrdinalIgnoreCase</c> as a constant, which a row marked
    /// <see cref="RuntimeChange.OrdinalUnaffected"/> does not match (ticket P2-075).
    /// </summary>
    public bool TryMatch(CallIdentity identity, RuntimeInterval interval, ImmutableArray<string> suppressed, bool ordinalComparison, out RuntimeChange match) =>
        TryMatch(identity, interval, suppressed, ordinalComparison, [], out match);

    /// <summary>
    /// As the four-argument overload, for one call with <paramref name="arguments"/> (ticket P2-073): a row with a
    /// <see cref="RuntimeChange.Precondition"/> does not match a call whose constant arguments cannot reach its change.
    /// Without arguments every such row matches.
    /// </summary>
    public bool TryMatch(CallIdentity identity, RuntimeInterval interval, ImmutableArray<string> suppressed, ImmutableArray<CallArgument> arguments, out RuntimeChange match) =>
        TryMatch(identity, interval, suppressed, ordinalComparison: false, arguments, out match);

    /// <summary>One call's match, by both what <paramref name="ordinalComparison"/> and what its <paramref name="arguments"/> say of it (tickets P2-075 and P2-073).</summary>
    public bool TryMatch(
        CallIdentity identity, RuntimeInterval interval, ImmutableArray<string> suppressed, bool ordinalComparison, ImmutableArray<CallArgument> arguments, out RuntimeChange match)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(interval);

        match = Rows.FirstOrDefault(row =>
            AppliesWithin(row, interval)
            && !(ordinalComparison && row.OrdinalUnaffected)
            && identity.Value.StartsWith(row.Member, StringComparison.Ordinal)
            && !suppressed.Contains(row.Member, StringComparer.Ordinal)
            && (row.Precondition is not { } precondition || RuntimeChangeReach.CanReach(precondition, arguments)))!;
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
    /// Parses a table in the embedded resource's format: an object with <c>coveredFrom</c>, <c>rows</c> and, optionally,
    /// <c>validPaths</c> (its <c>sources</c>, <c>validators</c>, <c>transforms</c>, <c>prefixes</c> and <c>guards</c>, each a
    /// list of member prefixes; tickets P2-074 and P2-188). A row whose
    /// <c>source</c> is missing, or is not <c>curated</c>, <c>documented</c> or <c>measured</c> (ADR 0035), or whose
    /// <c>changedIn</c> is missing or is neither null nor a .NET Framework or .NET target framework moniker (ADR 0040),
    /// is rejected with <see cref="InvalidDataException"/>, as is a malformed <c>coveredFrom</c>, a <c>precondition</c>
    /// that is not one of <see cref="RuntimeChangePrecondition"/>'s names (ticket P2-073) or a <c>requires</c> that is not
    /// <c>asyncStreamOperation</c>.
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
                Precondition = ParsePrecondition(member, element),

                OrdinalUnaffected = element.TryGetProperty("ordinalUnaffected", out JsonElement ordinal) && ordinal.GetBoolean(),
                Requires = ParseRequires(member, element),

            });
        }

        return new RuntimeChangeTable(coveredFromRuntime, builder.ToImmutable(), ParseValidPaths(root));
    }

    private static ValidPaths ParseValidPaths(JsonElement root) =>
        root.TryGetProperty("validPaths", out JsonElement lists)
            ? new ValidPaths(Members(lists, "sources"), Members(lists, "validators"), Members(lists, "transforms"), Members(lists, "prefixes"), Members(lists, "guards"))
            : new ValidPaths([], [], [], [], []);

    private static ImmutableArray<string> Members(JsonElement lists, string name) =>
        [.. lists.GetProperty(name).EnumerateArray().Select(static member => member.GetString()!)];

    private static TargetRuntime? ParseChangedIn(string member, JsonElement element) =>
        !element.TryGetProperty("changedIn", out JsonElement value)
            ? throw new InvalidDataException($"runtime-changes row '{member}' has no changedIn")
            : value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when TargetRuntime.Parse(value.GetString()!) is { } runtime => runtime,
                _ => throw new InvalidDataException($"runtime-changes row '{member}' has malformed changedIn {value.GetRawText()}"),
            };

    private static RuntimeChangePrecondition? ParsePrecondition(string member, JsonElement element)
    {
        string? precondition = element.TryGetProperty("precondition", out JsonElement value) ? value.GetString() : null;
        return precondition switch
        {
            null => null,
            "caseInsensitivePattern" => RuntimeChangePrecondition.CaseInsensitivePattern,
            "twoDigitYearFormat" => RuntimeChangePrecondition.TwoDigitYearFormat,
            "cultureSensitiveText" => RuntimeChangePrecondition.CultureSensitiveText,
            "invalidPath" => RuntimeChangePrecondition.InvalidPath,
            "trimEntries" => RuntimeChangePrecondition.TrimEntries,
            _ => throw new InvalidDataException($"runtime-changes row '{member}' has unknown precondition '{precondition}'"),
        };
    }

    private static RuntimeChangeWitness? ParseWitness(JsonElement element) =>
        element.TryGetProperty("witness", out JsonElement witness) ? BuildWitness(witness) : null;

    private static RuntimeChangeWitness BuildWitness(JsonElement witness) => new(
        [.. witness.GetProperty("input").EnumerateArray().Select(static v => v.GetRawText())],
        witness.GetProperty("culture").GetString()!,
        witness.GetProperty("legacy").GetRawText(),
        witness.GetProperty("modern").GetRawText());

    private static RowPrecondition? ParseRequires(string member, JsonElement element) =>
        element.TryGetProperty("requires", out JsonElement value) ? Requires(member, value) : null;

    private static RowPrecondition Requires(string member, JsonElement value) =>
        value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "asyncStreamOperation", StringComparison.Ordinal)
            ? RowPrecondition.AsyncStreamOperation
            : throw new InvalidDataException($"runtime-changes row '{member}' has unknown requires {value.GetRawText()}");

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

    /// <summary>The members <c>validPaths</c> lists (tickets P2-074 and P2-188), each a prefix matched as a row's <see cref="RuntimeChange.Member"/> is.</summary>
    private sealed record ValidPaths(
        ImmutableArray<string> Sources, ImmutableArray<string> Validators, ImmutableArray<string> Transforms, ImmutableArray<string> Prefixes, ImmutableArray<string> Guards);
}
