using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.ApiEquivalences;

using Xunit;

namespace Equiv.Core.Tests.ApiEquivalences;

/// <summary>
/// Ticket P2-143: the catalogue's entries for a <c>params T[]</c> member that .NET 9 rebinds to the
/// <c>params ReadOnlySpan&lt;T&gt;</c> overload, and the <c>rest</c> adapter item they pass the elements on with.
/// </summary>
public sealed class ParamsSpanEntriesTests
{
    /// <summary>
    /// Each entry passes the arguments before the <c>params</c> elements through and the elements as the span, and names
    /// .NET 9, which added the span overloads, as the runtime a pair must cross for it to apply.
    /// </summary>
    [Theory]
    [InlineData(
        "bcl.string-format-provider-params-span",
        "System.String::Format(System.IFormatProvider,string,object[])",
        "System.String::Format(System.IFormatProvider,string,System.ReadOnlySpan<object>)",
        2,
        "https://learn.microsoft.com/en-us/dotnet/api/system.string.format")]
    [InlineData(
        "bcl.string-join-char-params-span",
        "System.String::Join(char,string[])",
        "System.String::Join(char,System.ReadOnlySpan<string>)",
        1,
        "https://learn.microsoft.com/en-us/dotnet/api/system.string.join")]
    [InlineData(
        "bcl.string-builder-append-format-provider-params-span",
        "System.Text.StringBuilder::AppendFormat(System.IFormatProvider,string,object[])",
        "System.Text.StringBuilder::AppendFormat(System.IFormatProvider,string,System.ReadOnlySpan<object>)",
        3,
        "https://learn.microsoft.com/en-us/dotnet/api/system.text.stringbuilder.appendformat")]
    [InlineData(
        "bcl.path-combine-params-span",
        "System.IO.Path::Combine(string[])",
        "System.IO.Path::Combine(System.ReadOnlySpan<string>)",
        0,
        "https://learn.microsoft.com/en-us/dotnet/api/system.io.path.combine")]
    public void Table_HasTheParamsSpanEntries(string id, string legacy, string modern, int rest, string page)
    {
        ApiEquivalence entry = ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

        Assert.Equal((legacy, modern, page), (entry.Legacy, entry.Modern, entry.Url.OriginalString));
        Assert.Equal([.. Enumerable.Range(0, rest).Select(static i => new ApiArgument(i)), new ApiArgument(rest, Rest: true)], entry.Arguments);
        Assert.Contains("null array, which cannot happen here", entry.Reason, StringComparison.Ordinal);
        Assert.Equal(TargetRuntime.Parse("net9.0"), entry.AddedIn);
    }

    /// <summary>
    /// A <c>rest</c> item takes every source argument from its position on, so it is an entry's last item and the items
    /// before it are the positions before it, each once.
    /// </summary>
    [Fact]
    public void Table_ARestItemIsLastAndFollowsThePositionsBeforeIt()
    {
        ImmutableArray<ApiEquivalence> spans = [.. ApiEquivalenceTable.Load().Entries.Where(static entry => entry.Arguments.Any(static a => a.Rest))];

        Assert.Equal(4, spans.Length);
        Assert.All(spans, static entry =>
        {
            ApiArgument rest = entry.Arguments[^1];
            Assert.True(rest.Rest);
            Assert.Equal(Enumerable.Range(0, entry.Arguments.Length - 1).Select(static i => (int?)i), entry.Arguments[..^1].Select(static a => a.Source));
            Assert.Equal(entry.Arguments.Length - 1, rest.Source);
        });
    }

    [Fact]
    public void Parse_ReadsARestItem()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Parse("""
            [
              { "id": "m", "legacy": "A::F(int,object[])", "modern": "A::F(int,System.ReadOnlySpan<object>)", "reason": "r", "url": "https://learn.microsoft.com/x",
                "arguments": [{ "arg": 0 }, { "rest": 1 }] }
            ]
            """);

        Assert.Equal([new ApiArgument(0), new ApiArgument(1, Rest: true)], Assert.Single(table.Entries).Arguments);
        Assert.NotEqual(new ApiArgument(1), table.Entries[0].Arguments[1]);
    }
}
