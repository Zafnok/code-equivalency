using Microsoft.CodeAnalysis.Sarif;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P2-117: the <c>generic-enum-members</c> sample, .NET 8 against .NET 10, where the <c>Type</c>-taking
/// <c>Enum.IsDefined</c> and <c>Enum.GetValues</c> calls become the generic ones an analyzer suggests.
/// </summary>
public sealed partial class SamplesEndToEndTests
{
    /// <summary>Criterion 1: both methods are Equivalent, each with the entry it used, and nothing is left opaque or rebound.</summary>
    [Theory]
    [InlineData("::Known(", "bcl.enum-is-defined-generic")]
    [InlineData("::Count(", "bcl.enum-get-values-generic")]
    public void GenericEnumMembers_ATypeTakingCallIsEquivalentToTheGenericOneWithTheEntryApplied(string procedure, string entry)
    {
        Result result = Single("generic-enum-members", procedure);

        Assert.Equal("EQ001", result.RuleId);
        Assert.Equal([entry], result.GetProperty<List<string>>("equivalencesApplied"), StringComparer.Ordinal);
        Assert.False(result.TryGetProperty("reboundCalls", out string? _));
        Assert.False(result.TryGetProperty("opaqueNodes", out string? _));
    }
}
