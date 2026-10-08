using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.ApiEquivalences;

using Xunit;

namespace Equiv.Core.Tests.ApiEquivalences;

/// <summary>
/// The catalogue's entries whose adapter states an integer range and whose modern member a runtime added (ADR 0020,
/// clarified for ticket P2-142): the five <c>TimeSpan</c> factories that .NET 9 gave integer overloads.
/// </summary>
public sealed class ApiIntegerRangeTests
{
    private static readonly TargetRuntime Net9 = TargetRuntime.Parse("net9.0")!;

    /// <summary>
    /// Each entry takes the argument without its conversion to <c>double</c>, as an integer of the modern parameter's width
    /// inside the range on which the two overloads agree: the range of <c>TimeSpan</c> for days and hours, every 32-bit
    /// value for the others.
    /// </summary>
    [Theory]
    [InlineData("bcl.timespan-from-days-integer", "System.TimeSpan::FromDays(double)", "System.TimeSpan::FromDays(int)", 32, -10675199L, 10675199L)]
    [InlineData("bcl.timespan-from-hours-integer", "System.TimeSpan::FromHours(double)", "System.TimeSpan::FromHours(int)", 32, -256204778L, 256204778L)]
    [InlineData("bcl.timespan-from-minutes-integer", "System.TimeSpan::FromMinutes(double)", "System.TimeSpan::FromMinutes(long)", 64, int.MinValue, int.MaxValue)]
    [InlineData("bcl.timespan-from-seconds-integer", "System.TimeSpan::FromSeconds(double)", "System.TimeSpan::FromSeconds(long)", 64, int.MinValue, int.MaxValue)]
    [InlineData("bcl.timespan-from-milliseconds-integer-and-microseconds", "System.TimeSpan::FromMilliseconds(double)", "System.TimeSpan::FromMilliseconds(long,long)", 64, int.MinValue, int.MaxValue)]
    [InlineData("bcl.timespan-from-milliseconds-integer", "System.TimeSpan::FromMilliseconds(double)", "System.TimeSpan::FromMilliseconds(long)", 64, int.MinValue, int.MaxValue, "net10.0")]
    public void Table_HasTheTimeSpanFactoryEntries_EachWithItsRange(string id, string legacy, string modern, int bits, long min, long max, string addedIn = "net9.0")
    {
        ApiEquivalence entry = ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

        Assert.Equal((legacy, modern), (entry.Legacy, entry.Modern));
        Assert.Equal(TargetRuntime.Parse(addedIn), entry.AddedIn);
        Assert.Equal(new ApiArgument(0, Unwrap: true) { Range = new ApiIntegerRange(bits, min, max) }, entry.Arguments[0]);
        Assert.Contains("OverflowException", entry.Reason, StringComparison.Ordinal);
        Assert.Contains("ArgumentOutOfRangeException", entry.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ranges are what <c>TimeSpan</c> holds: the largest whole number of days and of hours whose ticks fit a
    /// <c>long</c>, on both sides of zero. One more does not fit.
    /// </summary>
    [Theory]
    [InlineData("bcl.timespan-from-days-integer", 864_000_000_000L)]
    [InlineData("bcl.timespan-from-hours-integer", 36_000_000_000L)]
    public void Table_TheDaysAndHoursRangesAreTheRangeOfTimeSpan(string id, long ticksPerUnit)
    {
        ApiIntegerRange range = ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal)).Arguments[0].Range!;

        Assert.Equal(long.MaxValue / ticksPerUnit, range.Max);
        Assert.Equal(-range.Max, range.Min);
    }

    /// <summary>
    /// .NET 9's <c>FromMilliseconds(long, long)</c> takes its second argument's default, and every other entry has one
    /// argument. .NET 10 added <c>FromMilliseconds(long)</c>, which an integer binds from then on: its entry comes first, so
    /// a pair whose modern side has both overloads takes it.
    /// </summary>
    [Fact]
    public void Table_MillisecondsHasAnEntryPerRuntime_TheLaterRuntimeFirst()
    {
        ImmutableArray<ApiEquivalence> factories = [.. ApiEquivalenceTable.Load().Entries.Where(static e => e.Id.StartsWith("bcl.timespan-from-", StringComparison.Ordinal))];

        Assert.Equal(6, factories.Length);
        ImmutableArray<ApiEquivalence> milliseconds = [.. factories.Where(static e => e.Legacy.Contains("FromMilliseconds", StringComparison.Ordinal))];
        Assert.Equal(
            ["bcl.timespan-from-milliseconds-integer", "bcl.timespan-from-milliseconds-integer-and-microseconds"],
            milliseconds.Select(static e => e.Id),
            StringComparer.Ordinal);
        Assert.True(milliseconds[0].AddedIn > milliseconds[1].AddedIn);
        Assert.Equal([new ApiArgument(Source: null, ConstantType: "bv64", Constant: "0")], milliseconds[1].Arguments.Skip(1));
        Assert.All(factories.Where(e => e != milliseconds[1]), static e => Assert.Single(e.Arguments));
    }

    /// <summary>No entry from before the ticket names a runtime or a range.</summary>
    /// <summary>The <c>params</c> span entries of ticket P2-143 name a runtime too, and are pinned by their own tests.</summary>
    [Fact]
    public void Table_EveryOtherEntryHasNoRangeAndNoRuntime() =>
        Assert.All(
            ApiEquivalenceTable.Load().Entries.Where(static e => !e.Id.StartsWith("bcl.timespan-from-", StringComparison.Ordinal)),
            static e =>
            {
                Assert.Equal(e.Id.EndsWith("-params-span", StringComparison.Ordinal), e.AddedIn is not null);
                Assert.All(e.Arguments, static a => Assert.Null(a.Range));
            });

    [Fact]
    public void Parse_ReadsAnIntegerRangeAndTheRuntimeThatAddedTheModernMember()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Parse("""
            [
              { "id": "m", "legacy": "A::F(double)", "modern": "A::F(int)", "addedIn": "net9.0", "reason": "r", "url": "https://learn.microsoft.com/x",
                "arguments": [{ "arg": 0, "unwrap": true, "integer": { "bits": 32, "min": -5, "max": 7 } }, { "arg": 1 }] },
              { "id": "n", "legacy": "A::G()", "modern": "A::H()", "reason": "r", "url": "https://learn.microsoft.com/x", "arguments": [] }
            ]
            """);

        Assert.Equal(Net9, table.Entries[0].AddedIn);
        Assert.Equal(
            [new ApiArgument(0, Unwrap: true) { Range = new ApiIntegerRange(32, -5, 7) }, new ApiArgument(1)],
            table.Entries[0].Arguments);
        Assert.Null(table.Entries[0].Arguments[1].Range);
        Assert.Null(table.Entries[1].AddedIn);
    }

    [Fact]
    public void Parse_RejectsARuntimeThatIsNoTargetFramework()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(static () => ApiEquivalenceTable.Parse("""
            [{ "id": "m", "legacy": "A::F()", "modern": "A::G()", "addedIn": "netstandard2.0", "reason": "r", "url": "https://learn.microsoft.com/x", "arguments": [] }]
            """));

        Assert.Equal("api-equivalences entry 'm' has malformed addedIn 'netstandard2.0'", error.Message);
    }

    /// <summary>An argument with a range is another argument than one without, and than one with another range.</summary>
    [Fact]
    public void AnArgumentsRangeIsPartOfItsEquality()
    {
        ApiArgument ranged = new(0, Unwrap: true) { Range = new ApiIntegerRange(32, 0, 1) };

        Assert.NotEqual(new ApiArgument(0, Unwrap: true), ranged);
        Assert.NotEqual(ranged with { Range = new ApiIntegerRange(32, 0, 2) }, ranged);
        Assert.Equal(ranged with { }, ranged);
    }

    /// <summary>
    /// An entry with no runtime applies to every pair; one with a runtime only to a pair whose interval crosses it, which a
    /// pair that ends before it, starts at it or is on one runtime does not.
    /// </summary>
    [Theory]
    [InlineData(null, "net48", "net8.0", true)]
    [InlineData(null, "net10.0", "net10.0", true)]
    [InlineData("net9.0", "net48", "net8.0", false)]
    [InlineData("net9.0", "net8.0", "net9.0", true)]
    [InlineData("net9.0", "net48", "net10.0", true)]
    [InlineData("net9.0", "net9.0", "net10.0", false)]
    [InlineData("net9.0", "net9.0", "net9.0", false)]
    public void AppliesWithin_APairThatCrossesTheRuntimeThatAddedTheModernMember(string? addedIn, string legacy, string modern, bool applies)
    {
        ApiEquivalence entry = new("m", IsType: false, "A::F()", "A::G()", [], "r", new Uri("https://learn.microsoft.com/x"))
        {
            AddedIn = addedIn is null ? null : TargetRuntime.Parse(addedIn),
        };

        Assert.Equal(applies, entry.AppliesWithin(new RuntimeInterval(TargetRuntime.Parse(legacy)!, TargetRuntime.Parse(modern)!)));
    }

    [Fact]
    public void AppliesWithin_RejectsNoInterval()
    {
        ApiEquivalence entry = new("m", IsType: false, "A::F()", "A::G()", [], "r", new Uri("https://learn.microsoft.com/x"));

        Assert.Throws<ArgumentNullException>(() => entry.AppliesWithin(null!));
    }
}
