using System.Globalization;
using System.Linq;

namespace Equiv.Core;

/// <summary>
/// The runtime a project runs on (ADR 0040 decision 1): .NET Framework 4.x or .NET (Core). Runtimes are totally ordered,
/// every .NET Framework version before every .NET (Core) version (decision 2), then by <see cref="Version"/>.
/// </summary>
public sealed record TargetRuntime(TargetRuntime.RuntimeFamily Family, Version Version) : IComparable<TargetRuntime>
{
    private const string FrameworkIdentifier = ".NETFramework";
    private const string CoreIdentifier = ".NETCoreApp";
    private const string VersionKey = "Version=v";

    /// <summary>Which runtime line a <see cref="TargetRuntime"/> belongs to.</summary>
    public enum RuntimeFamily
    {
        NetFramework,
        NetCore,
    }

    /// <summary>
    /// A target framework moniker (<c>.NETFramework,Version=v4.8</c>, <c>.NETCoreApp,Version=v8.0</c>, a profile or
    /// platform suffix ignored) or a short name (<c>net48</c>, <c>netcoreapp3.1</c>, <c>net8.0</c>, <c>net8.0-windows</c>)
    /// as a runtime, or null when <paramref name="text"/> names neither .NET Framework nor .NET (Core), such as <c>netstandard2.0</c>.
    /// </summary>
    public static TargetRuntime? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();
        return trimmed.StartsWith('.') ? ParseMoniker(trimmed) : ParseShortName(trimmed);
    }

    /// <summary>The short name: <c>net48</c>, <c>net472</c>, <c>netcoreapp3.1</c>, <c>net8.0</c>.</summary>
    public override string ToString() => Family switch
    {
        RuntimeFamily.NetFramework => "net" + string.Concat(Components().Select(static c => c.ToString(CultureInfo.InvariantCulture))),
        _ when Version.Major < 5 => $"netcoreapp{Version.Major.ToString(CultureInfo.InvariantCulture)}.{Version.Minor.ToString(CultureInfo.InvariantCulture)}",
        _ => $"net{Version.Major.ToString(CultureInfo.InvariantCulture)}.{Version.Minor.ToString(CultureInfo.InvariantCulture)}",
    };

    public int CompareTo(TargetRuntime? other) =>
        other is null ? 1
        : Family != other.Family ? Family.CompareTo(other.Family)
        : Version.CompareTo(other.Version);

    public static bool operator <(TargetRuntime? left, TargetRuntime? right) => Compare(left, right) < 0;

    public static bool operator >(TargetRuntime? left, TargetRuntime? right) => Compare(left, right) > 0;

    public static bool operator <=(TargetRuntime? left, TargetRuntime? right) => Compare(left, right) <= 0;

    public static bool operator >=(TargetRuntime? left, TargetRuntime? right) => Compare(left, right) >= 0;

    private static int Compare(TargetRuntime? left, TargetRuntime? right) => left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    /// <summary>The version's defined components, without trailing zeros past the minor: 4.8 is <c>4, 8</c>, 4.7.2 is <c>4, 7, 2</c>.</summary>
    private int[] Components() =>
        Version.Build > 0 ? [Version.Major, Version.Minor, Version.Build] : [Version.Major, Version.Minor];

    private static TargetRuntime? ParseMoniker(string moniker)
    {
        string[] parts = moniker.Split(',', StringSplitOptions.TrimEntries);
        RuntimeFamily? family = parts[0] switch
        {
            FrameworkIdentifier => RuntimeFamily.NetFramework,
            CoreIdentifier => RuntimeFamily.NetCore,
            _ => null,
        };
        string? versionText = parts.Skip(1).FirstOrDefault(static p => p.StartsWith(VersionKey, StringComparison.OrdinalIgnoreCase))?[VersionKey.Length..];
        return family is { } f && versionText is not null && Version.TryParse(versionText, out Version? version) ? new TargetRuntime(f, version) : null;
    }

    private static TargetRuntime? ParseShortName(string shortName)
    {
        const string CorePrefix = "NETCOREAPP";
        const string NetPrefix = "NET";
        string name = shortName.Split('-')[0].ToUpperInvariant();
        if (name.StartsWith(CorePrefix, StringComparison.Ordinal))
        {
            return Version.TryParse(name[CorePrefix.Length..], out Version? core) ? new TargetRuntime(RuntimeFamily.NetCore, core) : null;
        }

        if (!name.StartsWith(NetPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string rest = name[NetPrefix.Length..];
        if (rest.Contains('.', StringComparison.Ordinal))
        {
            return Version.TryParse(rest, out Version? modern) && modern.Major >= 5 ? new TargetRuntime(RuntimeFamily.NetCore, modern) : null;
        }

        // net48, net472: one digit per component, .NET Framework only.
        return rest.Length is >= 2 and <= 3 && rest.All(char.IsAsciiDigit)
            ? new TargetRuntime(RuntimeFamily.NetFramework, Version.Parse(string.Join('.', rest.ToCharArray())))
            : null;
    }
}
