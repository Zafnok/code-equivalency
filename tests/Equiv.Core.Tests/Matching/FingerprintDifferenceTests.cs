using Equiv.Core.Matching;

using Xunit;

namespace Equiv.Core.Tests.Matching;

/// <summary>Ticket P2-173: how a <see cref="FingerprintDifference"/> names the side or sides it concerns.</summary>
public sealed class FingerprintDifferenceTests
{
    [Theory]
    [InlineData(true, false, "legacy")]
    [InlineData(false, true, "modern")]
    [InlineData(true, true, "legacy, modern")]
    [InlineData(false, false, "")]
    public void SidesNamesEachSideAsked(bool legacy, bool modern, string expected) =>
        Assert.Equal(expected, FingerprintDifference.Sides(legacy, modern));
}
