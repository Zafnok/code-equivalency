using System.Collections.Immutable;

using Equiv.Core;

using Xunit;

namespace Equiv.Core.Tests;

public sealed class VerificationOptionsTests
{
    [Fact]
    public void OptionsWithEquivalentCallIdentityMapsAreEqual()
    {
        VerificationOptions a = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        VerificationOptions b = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void OptionsWithDifferentCallIdentityMapsAreUnequal()
    {
        VerificationOptions a = new(3, 5000, []);
        VerificationOptions b = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(4, 5000)]
    [InlineData(3, 6000)]
    public void OptionsWithDifferentBoundOrTimeoutAreUnequal(int bound, int timeoutMs)
    {
        VerificationOptions a = new(3, 5000, []);
        VerificationOptions b = new(bound, timeoutMs, []);
        Assert.NotEqual(a, b);
    }

    /// <summary>Ticket P1-001: <c>--chc-int-mode</c> is on unless turned off, and two options differing in it are unequal.</summary>
    [Fact]
    public void ChcIntModeIsOnByDefaultAndPartOfEquality()
    {
        VerificationOptions a = new(3, 5000, []);

        Assert.True(a.ChcIntMode);
        Assert.NotEqual(a, a with { ChcIntMode = false });
        Assert.Equal(a with { ChcIntMode = false }, new VerificationOptions(3, 5000, []) { ChcIntMode = false });
        Assert.Equal((a with { ChcIntMode = false }).GetHashCode(), new VerificationOptions(3, 5000, []) { ChcIntMode = false }.GetHashCode());
    }

    [Fact]
    public void OptionsAreNotEqualToNull()
    {
        Assert.False(new VerificationOptions(3, 5000, []).Equals(Null.Of<VerificationOptions>()));
    }
}
