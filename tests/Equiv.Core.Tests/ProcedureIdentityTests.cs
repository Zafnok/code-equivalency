using Xunit;

namespace Equiv.Core.Tests;

/// <summary>
/// <see cref="Location"/> is metadata a frontend attaches (ticket M2-002); it must not affect
/// equality or hashing, so identity matching by <see cref="ProcedureIdentity.Value"/> alone
/// (M1-003's <see cref="Matching.StableIdentityMatcher"/>) is unaffected by which side's location a
/// matched identity happens to carry.
/// </summary>
public sealed class ProcedureIdentityTests
{
    [Fact]
    public void SameValueIsEqualRegardlessOfLocation()
    {
        ProcedureIdentity withLocation = new("T::M()", new SourceSpan("a.cs", 1, 1, 1, 5));
        ProcedureIdentity withoutLocation = new("T::M()");

        Assert.Equal(withLocation, withoutLocation);
        Assert.Equal(withLocation.GetHashCode(), withoutLocation.GetHashCode());
    }

    [Fact]
    public void DifferentValueIsUnequal()
    {
        Assert.NotEqual(new ProcedureIdentity("T::M()"), new ProcedureIdentity("T::N()"));
    }

    [Fact]
    public void NotEqualToNull()
    {
        Assert.False(new ProcedureIdentity("T::M()").Equals(other: null));
    }

    [Fact]
    public void LocationDefaultsToNull()
    {
        Assert.Null(new ProcedureIdentity("T::M()").Location);
    }
}
