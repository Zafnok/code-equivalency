using System.Collections.Immutable;

using Equiv.Core.Configuration;

using Xunit;

namespace Equiv.Core.Tests.Configuration;

/// <summary>
/// <see cref="RenameMap"/>, <see cref="EquivConfig"/> and <see cref="EquivConfigResult"/> hold
/// <see cref="ImmutableDictionary{TKey,TValue}"/>/<see cref="ImmutableArray{T}"/> fields, which compare
/// by reference by default; these types override <c>Equals</c> for structural equality instead.
/// </summary>
public sealed class ConfigEqualityTests
{
    private static ImmutableDictionary<string, string> Map(params (string Key, string Value)[] entries) =>
        entries.ToImmutableDictionary(static e => e.Key, static e => e.Value, StringComparer.Ordinal);

    [Fact]
    public void RenameMapsWithTheSameEntriesAreEqualEvenAsDifferentDictionaryInstances()
    {
        RenameMap a = new(Map(("Old", "New")), Map());
        RenameMap b = new(Map(("Old", "New")), Map());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void RenameMapsWithDifferentCountsAreUnequal()
    {
        RenameMap a = new(Map(("Old", "New")), Map());
        RenameMap b = new(Map(("Old", "New"), ("Old2", "New2")), Map());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RenameMapsWithTheSameKeyDifferentValueAreUnequal()
    {
        RenameMap a = new(Map(("Old", "New")), Map());
        RenameMap b = new(Map(("Old", "Other")), Map());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RenameMapsWithDifferentTypeEntriesAreUnequal()
    {
        RenameMap a = new(Map(), Map(("Old.T", "New.T")));
        RenameMap b = new(Map(), Map());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RenameMapIsNotEqualToNull()
    {
        RenameMap a = new(Map(), Map());
        Assert.False(a.Equals(Null.Of<RenameMap>()));
    }

    [Fact]
    public void EquivConfigsWithEquivalentRenamesAndDictionariesAreEqual()
    {
        EquivConfig a = new(new RenameMap(Map(("A", "B")), Map()), Map(("C", "D")), 3, 5000);
        EquivConfig b = new(new RenameMap(Map(("A", "B")), Map()), Map(("C", "D")), 3, 5000);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [MemberData(nameof(UnequalConfigs))]
    public void EquivConfigsThatDifferInAnyFieldAreUnequal(EquivConfig a, EquivConfig b)
    {
        Assert.NotEqual(a, b);
    }

    public static TheoryData<EquivConfig, EquivConfig> UnequalConfigs()
    {
        EquivConfig baseline = new(RenameMap.Empty, Map(), 3, 5000);
        return new TheoryData<EquivConfig, EquivConfig>
        {
            { baseline, baseline with { Renames = new RenameMap(Map(("A", "B")), Map()) } },
            { baseline, baseline with { CallIdentityRenames = Map(("A", "B")) } },
            { baseline, baseline with { Bound = 4 } },
            { baseline, baseline with { TimeoutMs = 6000 } },
        };
    }

    [Fact]
    public void EquivConfigIsNotEqualToNull()
    {
        Assert.False(EquivConfig.Default.Equals(Null.Of<EquivConfig>()));
    }

    [Fact]
    public void EquivConfigResultsWithEquivalentDiagnosticsAreEqual()
    {
        EquivConfigResult a = new(EquivConfig.Default, [new EquivConfigDiagnostic("CFG001", "/", "bad")]);
        EquivConfigResult b = new(EquivConfig.Default, [new EquivConfigDiagnostic("CFG001", "/", "bad")]);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EquivConfigResultsWithDifferentDiagnosticsAreUnequal()
    {
        EquivConfigResult a = new(EquivConfig.Default, []);
        EquivConfigResult b = new(EquivConfig.Default, [new EquivConfigDiagnostic("CFG001", "/", "bad")]);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EquivConfigResultIsNotEqualToNull()
    {
        Assert.False(new EquivConfigResult(EquivConfig.Default, []).Equals(Null.Of<EquivConfigResult>()));
    }
}
