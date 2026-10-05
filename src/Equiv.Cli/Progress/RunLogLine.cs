using System.Globalization;
using System.Text;

using Equiv.Core.Progress;

namespace Equiv.Cli.Progress;

/// <summary>
/// A progress line in ADR 0038's fixed grammar, which <c>corpus.ps1</c> parses:
/// <c>equiv: +HH:MM:SS &lt;phase&gt; &lt;done&gt;/&lt;total&gt; (&lt;pct&gt;%) [item=&lt;identity&gt;] [outcome=&lt;o&gt;] [took=&lt;s&gt;] eta=&lt;dur&gt;|eta=? [worst=&lt;dur&gt;] [rate=&lt;n&gt;/s] [slow]</c>.
/// <c>+HH:MM:SS</c> is the time since the run started, <c>pct</c> is the share of the phase's weight done, a
/// <c>&lt;dur&gt;</c> is <c>HH:MM:SS.fff</c>, <c>took</c> is seconds with three decimals, and <c>rate</c> is items per
/// second with one decimal, all in the invariant culture. <see cref="PhaseEnd"/> and <see cref="Detail"/> are the two
/// other line shapes.
/// </summary>
internal sealed record RunLogLine(TimeSpan At, string Phase, int Done, int Total, long DoneWeight, long TotalWeight)
{
    private const string Prefix = "equiv: ";

    public string? Item { get; init; }

    public string? Outcome { get; init; }

    public TimeSpan? Took { get; init; }

    public TimeSpan? Eta { get; init; }

    public TimeSpan? Worst { get; init; }

    public double? Rate { get; init; }

    public bool Slow { get; init; }

    /// <summary>The share of the weight done, as a whole percentage; a phase without weight is complete.</summary>
    public long Percent => TotalWeight <= 0 ? 100 : DoneWeight * 100 / TotalWeight;

    /// <summary><c>equiv: +HH:MM:SS &lt;phase&gt; done in &lt;dur&gt;; eta@25%=&lt;dur&gt; eta@50%=&lt;dur&gt; eta@75%=&lt;dur&gt; dropped=&lt;n&gt;</c>; a quarter with no estimate is <c>?</c>.</summary>
    public static string PhaseEnd(TimeSpan at, string phase, TimeSpan took, EtaEstimator estimator, long dropped)
    {
        ArgumentNullException.ThrowIfNull(estimator);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{Stamp(at)} {phase} done in {Duration(took)}; eta@25%={Maybe(estimator.At25)} eta@50%={Maybe(estimator.At50)} eta@75%={Maybe(estimator.At75)} dropped={dropped}");
    }

    /// <summary>
    /// <c>equiv: +HH:MM:SS &lt;phase&gt; detail: &lt;text&gt; [item=&lt;identity&gt;]</c>. <paramref name="item"/> is the item
    /// the detail is about, which runs to the end of the line; with several items in flight their details interleave
    /// (ticket P2-077).
    /// </summary>
    public static string Detail(TimeSpan at, string phase, string text, string? item = null) =>
        $"{Prefix}{Stamp(at)} {phase} detail: {text}{(item is null ? string.Empty : $" item={item}")}";

    /// <summary><c>+HH:MM:SS</c>, the hours not wrapping at a day.</summary>
    public static string Stamp(TimeSpan at) =>
        string.Create(CultureInfo.InvariantCulture, $"+{(long)at.TotalHours:00}:{at.Minutes:00}:{at.Seconds:00}");

    /// <summary><c>HH:MM:SS.fff</c>, the hours not wrapping at a day.</summary>
    public static string Duration(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}");

    public string Format()
    {
        StringBuilder line = new();
        line.Append(CultureInfo.InvariantCulture, $"{Prefix}{Stamp(At)} {Phase} {Done}/{Total} ({Percent}%)");
        if (Item is not null)
        {
            line.Append(" item=").Append(Item);
        }

        if (Outcome is not null)
        {
            line.Append(" outcome=").Append(Outcome);
        }

        if (Took is { } took)
        {
            line.Append(CultureInfo.InvariantCulture, $" took={took.TotalSeconds:0.000}");
        }

        line.Append(" eta=").Append(Maybe(Eta));
        if (Worst is { } worst)
        {
            line.Append(" worst=").Append(Duration(worst));
        }

        if (Rate is { } rate)
        {
            line.Append(CultureInfo.InvariantCulture, $" rate={rate:0.0}/s");
        }

        if (Slow)
        {
            line.Append(" slow");
        }

        return line.ToString();
    }

    private static string Maybe(TimeSpan? duration) => duration is { } known ? Duration(known) : "?";
}
