namespace Equiv.Core;

/// <summary>
/// The runtimes a matched pair crosses (ADR 0040 decision 2): the half-open interval <c>(Older, Newer]</c>
/// between its two sides' <see cref="TargetRuntime"/>s, built in either order. A change point applies to the
/// pair only when the interval <see cref="Crosses(TargetRuntime)"/> it; a same-runtime pair
/// <see cref="IsEmpty"/> and crosses nothing.
/// </summary>
public sealed record RuntimeInterval
{
    /// <summary>The interval between <paramref name="first"/> and <paramref name="second"/>, whichever is older.</summary>
    public RuntimeInterval(TargetRuntime first, TargetRuntime second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        (Older, Newer) = first <= second ? (first, second) : (second, first);
    }

    /// <summary>The older side's runtime, excluded from the interval.</summary>
    public TargetRuntime Older { get; }

    /// <summary>The newer side's runtime, included in the interval.</summary>
    public TargetRuntime Newer { get; }

    /// <summary>True when both sides run on the same runtime, so no change point lies between them.</summary>
    public bool IsEmpty => Older.CompareTo(Newer) == 0;

    /// <summary>True when <paramref name="changePoint"/> lies in <c>(Older, Newer]</c>.</summary>
    public bool Crosses(TargetRuntime changePoint)
    {
        ArgumentNullException.ThrowIfNull(changePoint);
        return changePoint > Older && changePoint <= Newer;
    }

    /// <summary>
    /// The part of this interval a table covering .NET from <paramref name="coveredFrom"/> onward does not cover, or null.
    /// The .NET Framework to .NET boundary is covered (the table's <c>netcoreapp1.0</c> rows), so only a .NET (Core) side
    /// older than <paramref name="coveredFrom"/> leaves a gap: <c>netcoreapp2.1</c> to <c>net8.0</c> with coverage from
    /// <c>netcoreapp3.0</c> leaves <c>netcoreapp2.1</c>–<c>netcoreapp3.0</c>.
    /// </summary>
    public RuntimeInterval? UncoveredRange(TargetRuntime coveredFrom)
    {
        ArgumentNullException.ThrowIfNull(coveredFrom);

        bool gap = !IsEmpty && Older.Family == TargetRuntime.RuntimeFamily.NetCore && Older < coveredFrom;
        return gap ? new RuntimeInterval(Older, Newer < coveredFrom ? Newer : coveredFrom) : null;
    }
}
