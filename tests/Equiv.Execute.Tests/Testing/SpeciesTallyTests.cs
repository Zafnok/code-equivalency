using Equiv.Core.Execution;
using Equiv.Execute.Testing;

using Xunit;

namespace Equiv.Execute.Tests.Testing;

public sealed class SpeciesTallyTests
{
    /// <summary>Böhme, Liyanage and Wüstholz (FSE 2021): f1 / n.</summary>
    [Fact]
    public void GoodTuring_EstimatesFromSingletons()
    {
        SpeciesTally tally = new();
        Assert.Equal(1, tally.DiscoveryProbability);

        foreach (string species in new[] { "a", "b", "a", "c", "a", "d", "b" })
        {
            tally.Add(species);
        }

        Assert.Equal((7, 4, 2), (tally.Inputs, tally.Species, tally.Singletons));
        Assert.Equal(2 / 7d, tally.DiscoveryProbability);
        Assert.Equal(0.25, SpeciesTally.Estimate(8, 2));
    }

    [Fact]
    public void OutcomeClass_IsTheExceptionTypeOrAReturnBucket()
    {
        ExecutionInput input = new([]);

        Assert.Equal("threw \"System.Exception\"", OutcomeClass.Of(new ExecutionOutcome(input, "invariant", OutcomeKind.Threw, "\"System.Exception\"")));
        Assert.Equal("returned #12", OutcomeClass.Of(new ExecutionOutcome(input, "invariant", OutcomeKind.Returned, "1")));
        Assert.Equal("not-comparable", OutcomeClass.Of(new ExecutionOutcome(input, "invariant", OutcomeKind.NotComparable, "\"no answer\"")));
        Assert.Equal(5u, OutcomeClass.Bucket(string.Empty));
        Assert.All(Enumerable.Range(0, 200).Select(static i => OutcomeClass.Bucket(i.ToString(System.Globalization.CultureInfo.InvariantCulture))), static b => Assert.InRange(b, 0u, 15u));
        Assert.Equal(16, Enumerable.Range(0, 200).Select(static i => OutcomeClass.Bucket(i.ToString(System.Globalization.CultureInfo.InvariantCulture))).Distinct().Count());
    }
}
