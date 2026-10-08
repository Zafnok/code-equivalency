using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-143: the <c>params-span-overloads</c> sample, .NET 8 against .NET 10, where source that passes the elements
/// of a <c>params</c> parameter binds the array overload before and the <c>params ReadOnlySpan&lt;T&gt;</c> one after.
/// </summary>
public sealed partial class SamplesEndToEndTests
{
    /// <summary>Criterion 3: one method per entry, each Equivalent with the entry applied and no rebound call left.</summary>
    [Theory]
    [InlineData("::Describe(", "bcl.string-format-provider-params-span")]
    [InlineData("::Row(", "bcl.string-join-char-params-span")]
    [InlineData("::Append(", "bcl.string-builder-append-format-provider-params-span")]
    [InlineData("::Locate(", "bcl.path-combine-params-span")]
    public void ParamsSpanOverloads_ElementsPassedToEitherOverloadAreEquivalentWithTheEntryApplied(string procedure, string entry)
    {
        Result result = Single("params-span-overloads", procedure);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal([entry], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
        Assert.False(result.TryGetProperty("opaqueNodes", out string? _));
    }

    /// <summary>Criterion 4: the entry does not hide an edit. Two elements swapped on the modern side stay Divergent.</summary>
    [Fact]
    public void ParamsSpanOverloads_SwappedElementsOnTheModernSideAreDivergent()
    {
        Result result = Single("params-span-overloads", "::RowSwapped(");

        Assert.Equal("EQ002", result.RuleId);
        Assert.Equal(["bcl.string-join-char-params-span"], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
    }

    /// <summary>Criterion 4: a call that passes an array binds the array overload on both sides, and no entry rewrites it.</summary>
    [Fact]
    public void ParamsSpanOverloads_ACallThatPassesAnArrayIsNotRewritten()
    {
        Result result = Single("params-span-overloads", "::RowOfArray(");

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal("congruence", result.GetProperty<string>("proofMethod"));
        Assert.False(result.TryGetProperty("equivalencesApplied", out string? _));
    }
}
