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
        VerificationOptions a = new(3, 5000, ImmutableDictionary<string, string>.Empty);
        VerificationOptions b = new(3, 5000, ImmutableDictionary<string, string>.Empty.Add("Old::M", "New::M"));
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(4, 5000)]
    [InlineData(3, 6000)]
    public void OptionsWithDifferentBoundOrTimeoutAreUnequal(int bound, int timeoutMs)
    {
        VerificationOptions a = new(3, 5000, ImmutableDictionary<string, string>.Empty);
        VerificationOptions b = new(bound, timeoutMs, ImmutableDictionary<string, string>.Empty);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void OptionsAreNotEqualToNull()
    {
        Assert.False(new VerificationOptions(3, 5000, ImmutableDictionary<string, string>.Empty).Equals(Null.Of<VerificationOptions>()));
    }
}
