using CsCheck;

using Equiv.Core.Progress;

using Xunit;

namespace Equiv.Core.Tests.Progress;

/// <summary>Ticket M4-012 criterion 6: the thresholds of <see cref="EtaEstimator"/>, its sign, its exactness, and its worst-case bound.</summary>
public sealed class EtaEstimatorTests
{
    private static readonly Gen<long> Ticks = Gen.Long[0, 1_000_000_000_000];

    [Fact]
    public void IsNullBelowBothThresholds()
    {
        Gen.Long[100, 1_000_000]
            .SelectMany(static total => Gen.Select(
                Gen.Long[0, ((total * EtaEstimator.MinPercent) - 1) / 100],
                Gen.Int[0, EtaEstimator.MinItems - 1],
                Ticks,
                (done, items, ticks) => (Total: total, Done: done, Items: items, Ticks: ticks)))
            .Sample(static s => Assert.Null(EtaEstimator.Estimate(TimeSpan.FromTicks(s.Ticks), s.Done, s.Total, s.Items)), iter: 1000);
    }

    [Fact]
    public void IsNonNegativeAboveTheWeightThreshold()
    {
        Gen.Long[1, 1_000_000]
            .SelectMany(static total => Gen.Select(
                Gen.Long[Math.Max(1, ((total * EtaEstimator.MinPercent) + 99) / 100), total],
                Gen.Int[0, 1000],
                Ticks,
                (done, items, ticks) => (Total: total, Done: done, Items: items, Ticks: ticks)))
            .Sample(
                static s =>
                {
                    TimeSpan? eta = EtaEstimator.Estimate(TimeSpan.FromTicks(s.Ticks), s.Done, s.Total, s.Items);
                    Assert.NotNull(eta);
                    Assert.True(eta >= TimeSpan.Zero);
                },
                iter: 1000);
    }

    [Fact]
    public void IsNonNegativeAboveTheItemThreshold()
    {
        Gen.Select(Gen.Long[1, 1_000_000], Gen.Long[1, 1_000_000], Gen.Int[EtaEstimator.MinItems, 10_000], Ticks)
            .Sample(
                static (done, extra, items, ticks) =>
                {
                    TimeSpan? eta = EtaEstimator.Estimate(TimeSpan.FromTicks(ticks), done, done + (extra * 1000), items);
                    Assert.NotNull(eta);
                    Assert.True(eta >= TimeSpan.Zero);
                },
                iter: 1000);
    }

    [Fact]
    public void IsExactForAConstantCostPerUnitOfWeight()
    {
        Gen.Long[1, 1_000_000]
            .SelectMany(static total => Gen.Select(Gen.Long[1, total], Gen.Long[0, 10_000_000], (done, cost) => (Total: total, Done: done, Cost: cost)))
            .Sample(
                static s => Assert.Equal(
                    TimeSpan.FromTicks(s.Cost * (s.Total - s.Done)),
                    EtaEstimator.Estimate(TimeSpan.FromTicks(s.Cost * s.Done), s.Done, s.Total, EtaEstimator.MinItems)),
                iter: 1000);
    }

    /// <summary>
    /// When no item took longer than its timeout bound, an estimate clamped to the worst case of the pairs left never
    /// exceeds it whatever the weights, and with equal weights the unclamped estimate does not either.
    /// </summary>
    [Fact]
    public void IsNeverMoreThanTheWorstCaseWhenEveryItemIsWithinItsBound()
    {
        Gen.Select(Gen.Int[1, 10_000], Gen.Int[1, 5], Gen.Int[1, 60])
            .SelectMany(static (timeout, rungs, n) => Gen.Select(
                Gen.Long[1, 1000].Array[n],
                Gen.Long[0, (long)timeout * rungs * TimeSpan.TicksPerMillisecond].Array[n],
                Gen.Int[1, n],
                (weights, times, done) => (Timeout: timeout, Rungs: rungs, N: n, Weights: weights, Times: times, Done: done)))
            .Sample(
                static s =>
                {
                    TimeSpan worst = EtaEstimator.WorstCase(s.N - s.Done, s.Timeout, s.Rungs);
                    TimeSpan elapsed = TimeSpan.FromTicks(s.Times.Take(s.Done).Sum());
                    TimeSpan? clamped = EtaEstimator.Estimate(elapsed, s.Weights.Take(s.Done).Sum(), s.Weights.Sum(), s.Done, worst);
                    TimeSpan? equal = EtaEstimator.Estimate(elapsed, s.Done, s.N, s.Done);

                    Assert.True(clamped is null || clamped <= worst);
                    Assert.True(equal is null || equal <= worst);
                },
                iter: 1000);
    }

    /// <summary>Ticket M4-016: CsCheck seed 571VKAzBWW29 shrunk to this; the quotient is past <see cref="long.MaxValue"/> ticks.</summary>
    [Fact]
    public void SaturatesInsteadOfWrappingWhenTheEstimateOverflows()
    {
        TimeSpan elapsed = TimeSpan.FromTicks(154_402_185_409);

        Assert.Equal(TimeSpan.MaxValue, EtaEstimator.Estimate(elapsed, doneWeight: 9, totalWeight: 785_039_009, doneItems: 8559));
        Assert.Equal(TimeSpan.FromHours(1), EtaEstimator.Estimate(elapsed, doneWeight: 9, totalWeight: 785_039_009, doneItems: 8559, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void IsNullWhenNoWeightIsDone()
    {
        Assert.Null(EtaEstimator.Estimate(TimeSpan.FromSeconds(1), doneWeight: 0, totalWeight: 0, doneItems: 50));
    }

    [Fact]
    public void WorstCaseIsPairsTimesTimeoutTimesRungs()
    {
        Assert.Equal(TimeSpan.FromSeconds(3 * 5 * 4), EtaEstimator.WorstCase(3, 5000, 4));
    }

    [Fact]
    public void ObserveRecordsTheEstimateGivenAtEachQuarter()
    {
        EtaEstimator estimator = new(100);

        Assert.Null(estimator.Observe(TimeSpan.FromSeconds(1), doneWeight: 1, doneItems: 1));
        Assert.Equal(TimeSpan.FromSeconds(75), estimator.Observe(TimeSpan.FromSeconds(25), doneWeight: 25, doneItems: 2));
        Assert.Equal(TimeSpan.FromSeconds(10), estimator.Observe(TimeSpan.FromSeconds(60), doneWeight: 30, doneItems: 3, ceiling: TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(20), estimator.Observe(TimeSpan.FromSeconds(80), doneWeight: 80, doneItems: 4));

        Assert.Equal(TimeSpan.FromSeconds(75), estimator.At25);
        Assert.Equal(TimeSpan.FromSeconds(20), estimator.At50);
        Assert.Equal(TimeSpan.FromSeconds(20), estimator.At75);
    }

    [Fact]
    public void AQuarterNeverReachedHasNoCheckpoint()
    {
        EtaEstimator estimator = new(100);

        _ = estimator.Observe(TimeSpan.FromSeconds(30), doneWeight: 30, doneItems: 1);

        Assert.Equal(TimeSpan.FromSeconds(70), estimator.At25);
        Assert.Null(estimator.At50);
        Assert.Null(estimator.At75);
    }
}
