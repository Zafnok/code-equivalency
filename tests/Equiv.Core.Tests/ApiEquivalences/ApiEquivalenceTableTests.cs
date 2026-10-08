using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.ApiEquivalences;

using Xunit;

namespace Equiv.Core.Tests.ApiEquivalences;

/// <summary>The API-equivalence catalogue (ticket M3-009; ADR 0020).</summary>
public sealed class ApiEquivalenceTableTests
{
    [Fact]
    public void Table_EveryEntryHasReasonAndLearnUrl()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Load();

        Assert.NotEmpty(table.Entries);
        Assert.Same(table, ApiEquivalenceTable.Load());
        Assert.All(table.Entries, static entry => Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"{entry.Id} has no reason"));
        Assert.All(table.Entries, static entry => Assert.StartsWith("https://learn.microsoft.com/", entry.Url.OriginalString, StringComparison.Ordinal));
    }

    [Fact]
    public void Table_IdsAreUnique()
    {
        ImmutableArray<string> ids = [.. ApiEquivalenceTable.Load().Entries.Select(static entry => entry.Id)];
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Table_WebApiEntriesSayTheyEquateActionResultsNotSerializedResponses() =>
        Assert.All(
            ApiEquivalenceTable.Load().Entries.Where(static entry => entry.Id.StartsWith("webapi.", StringComparison.Ordinal)),
            static entry => Assert.Contains("equates action results", entry.Reason, StringComparison.Ordinal));

    [Fact]
    public void Table_HasTheTicketsMemberAndTypeEntries()
    {
        ImmutableArray<ApiEquivalence> entries = ApiEquivalenceTable.Load().Entries;

        Assert.Equal(10, entries.Count(static entry => !entry.IsType));
        Assert.Equal(5, entries.Count(static entry => entry.IsType));
        Assert.All(entries.Where(static entry => entry.IsType), static entry => Assert.Empty(entry.Arguments));
        Assert.DoesNotContain(entries, static entry => entry.Legacy.Contains("StatusCode", StringComparison.Ordinal));
    }

    /// <summary>
    /// Tickets P2-070 and P2-137: identical source that the modern reference assemblies bind to an added overload or a moved
    /// member. The two <c>TrimEnd</c> entries share a legacy member; the one-element entry comes first.
    /// </summary>
    [Theory]
    [InlineData("bcl.string-trim-end-one-char", "System.String::TrimEnd(char[])", "System.String::TrimEnd(char)", 2)]
    [InlineData("bcl.string-trim-end-no-chars", "System.String::TrimEnd(char[])", "System.String::TrimEnd()", 1)]
    [InlineData("bcl.string-trim-start-no-chars", "System.String::TrimStart(char[])", "System.String::TrimStart()", 1)]
    [InlineData("bcl.directory-info-full-name", "System.IO.DirectoryInfo::get_FullName()", "System.IO.FileSystemInfo::get_FullName()", 1)]
    public void Table_HasTheRebindingEntries_EachPassingItsSourceArgumentsThrough(string id, string legacy, string modern, int arguments)
    {
        ApiEquivalence entry = ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

        Assert.Equal((legacy, modern), (entry.Legacy, entry.Modern));
        Assert.Equal(Enumerable.Range(0, arguments).Select(static i => new ApiArgument(i)), entry.Arguments);
    }

    /// <summary>
    /// Ticket P2-137: entries that share a legacy member are tried in file order, and each must address another number of
    /// source arguments, or the later one could never apply.
    /// </summary>
    [Fact]
    public void Table_EntriesThatShareALegacyMemberAddressDifferentArgumentCounts()
    {
        ImmutableArray<ApiEquivalence> entries = ApiEquivalenceTable.Load().Entries;

        Assert.Equal(
            ["bcl.string-trim-end-one-char", "bcl.string-trim-end-no-chars"],
            entries.Where(static entry => string.Equals(entry.Legacy, "System.String::TrimEnd(char[])", StringComparison.Ordinal)).Select(static entry => entry.Id),
            StringComparer.Ordinal);
        Assert.All(
            entries.Where(static entry => !entry.IsType).GroupBy(static entry => entry.Legacy, StringComparer.Ordinal),
            static group => Assert.Equal(group.Count(), group.Select(static entry => entry.Arguments.Count(static a => a.Source is not null)).Distinct().Count()));
    }

    [Fact]
    public void Parse_ReadsMemberAndTypeEntriesAndEveryArgumentForm()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Parse("""
            // a comment
            [
              { "id": "m", "legacy": "A::F()", "modern": "B::G()", "reason": "r", "url": "https://learn.microsoft.com/x",
                "arguments": [{ "arg": 0 }, { "arg": 1, "unwrap": true }, { "arg": 2, "convertTo": "System.Object" }, { "const": 0, "type": "bv32" }, { "arg": 3, "unwrap": false }] },
              { "id": "t", "legacyType": "A", "modernType": "B", "reason": "r", "url": "https://learn.microsoft.com/y" }
            ]
            """);

        ApiEquivalence member = table.Entries[0];
        Assert.False(member.IsType);
        Assert.Equal(("m", "A::F()", "B::G()"), (member.Id, member.Legacy, member.Modern));
        Assert.Equal(
            [new ApiArgument(0), new ApiArgument(1, Unwrap: true), new ApiArgument(2, ConvertTo: "System.Object"), new ApiArgument(Source: null, ConstantType: "bv32", Constant: "0"), new ApiArgument(3)],
            member.Arguments);
        ApiEquivalence type = table.Entries[1];
        Assert.True(type.IsType);
        Assert.Equal(("A", "B", "https://learn.microsoft.com/y"), (type.Legacy, type.Modern, type.Url.OriginalString));
    }

    [Fact]
    public void Enabled_LeavesOutEveryEntryWhoseIdStartsWithASuppressedPrefix()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Load();

        Assert.Equal(table.Entries, table.Enabled([]));
        ImmutableArray<ApiEquivalence> enabled = table.Enabled(["webapi.", "bcl.string-split", "bcl.string-trim", "bcl.directory-info"]);
        Assert.Equal(["bcl.string-contains-char"], enabled.Select(static entry => entry.Id), StringComparer.Ordinal);
    }
}
