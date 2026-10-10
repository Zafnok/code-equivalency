using System.Linq;

using Equiv.Core.ApiEquivalences;

using Xunit;

namespace Equiv.Core.Tests.ApiEquivalences;

/// <summary>
/// Ticket P2-117: the catalogue's entries for the <c>Type</c>-taking <c>Enum</c> members whose generic forms an analyzer
/// suggests (CA2263), and the <c>typeArgument</c> adapter item that names the generic member's type argument.
/// </summary>
public sealed class GenericEnumEntriesTests
{
    private static ApiEquivalence Entry(string id) => ApiEquivalenceTable.Load().Entries.Single(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary><c>IsDefined</c> takes its type argument from the <c>typeof</c> and passes the value, unboxed, which must be of that enum.</summary>
    [Fact]
    public void Table_HasTheIsDefinedEntry()
    {
        ApiEquivalence entry = Entry("bcl.enum-is-defined-generic");

        Assert.Equal(("System.Enum::IsDefined(System.Type,object)", "System.Enum::IsDefined`1({T})<{T}>"), (entry.Legacy, entry.Modern));
        Assert.Equal([new ApiArgument(0) { TypeArgument = true }, new ApiArgument(1, Unwrap: true) { OfTypeArgument = true }], entry.Arguments);
        Assert.False(entry.ReturnsTypeArgumentArray);
        Assert.Null(entry.AddedIn);
        Assert.Equal("https://learn.microsoft.com/en-us/dotnet/api/system.enum.isdefined", entry.Url.OriginalString);
        Assert.Contains("neither throws", entry.Reason, StringComparison.Ordinal);
    }

    /// <summary><c>GetValues</c> passes nothing and returns the enum's array where the legacy member returns it as <c>Array</c>.</summary>
    [Fact]
    public void Table_HasTheGetValuesEntry()
    {
        ApiEquivalence entry = Entry("bcl.enum-get-values-generic");

        Assert.Equal(("System.Enum::GetValues(System.Type)", "System.Enum::GetValues`1()<{T}>"), (entry.Legacy, entry.Modern));
        Assert.Equal([new ApiArgument(0) { TypeArgument = true }], entry.Arguments);
        Assert.True(entry.ReturnsTypeArgumentArray);
        Assert.Null(entry.AddedIn);
        Assert.Equal("https://learn.microsoft.com/en-us/dotnet/api/system.enum.getvalues", entry.Url.OriginalString);
        Assert.Contains("neither throws", entry.Reason, StringComparison.Ordinal);
    }

    /// <summary>The modern member is named with the type argument in place of each <c>{T}</c>, and as written when there is none.</summary>
    [Fact]
    public void ModernOf_ReplacesEachPlaceholderWithTheTypeArgument()
    {
        ApiEquivalence entry = Entry("bcl.enum-is-defined-generic");

        Assert.Equal("System.Enum::IsDefined`1(System.DayOfWeek)<System.DayOfWeek>", entry.ModernOf("System.DayOfWeek"));
        Assert.Equal(entry.Modern, entry.ModernOf(typeArgument: null));
    }

    /// <summary>The three flags are read only when they are <c>true</c>, and a <c>typeArgument</c> item is its position.</summary>
    [Fact]
    public void Parse_ReadsTheTypeArgumentItemsAndFlags()
    {
        ApiEquivalenceTable table = ApiEquivalenceTable.Parse("""
            [
              { "id": "a", "legacy": "A::F(System.Type,object)", "modern": "A::F`1({T})<{T}>", "reason": "r", "url": "https://learn.microsoft.com/x",
                "returnsTypeArgumentArray": true,
                "arguments": [{ "typeArgument": 0 }, { "arg": 1, "ofTypeArgument": true }, { "arg": 2, "ofTypeArgument": false }] },
              { "id": "b", "legacy": "A::G()", "modern": "A::H()", "reason": "r", "url": "https://learn.microsoft.com/x",
                "returnsTypeArgumentArray": false, "arguments": [] }
            ]
            """);

        Assert.True(table.Entries[0].ReturnsTypeArgumentArray);
        Assert.Equal([new ApiArgument(0) { TypeArgument = true }, new ApiArgument(1) { OfTypeArgument = true }, new ApiArgument(2)], table.Entries[0].Arguments);
        Assert.False(table.Entries[1].ReturnsTypeArgumentArray);
    }

    /// <summary>An entry cannot return an array of a type argument it has no item for.</summary>
    [Fact]
    public void Parse_RejectsAnArrayOfATypeArgumentTheEntryDoesNotHave()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(static () => ApiEquivalenceTable.Parse("""
            [{ "id": "a", "legacy": "A::F()", "modern": "A::G()", "reason": "r", "url": "https://learn.microsoft.com/x",
               "returnsTypeArgumentArray": true, "arguments": [{ "arg": 0 }] }]
            """));

        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);
    }
}
