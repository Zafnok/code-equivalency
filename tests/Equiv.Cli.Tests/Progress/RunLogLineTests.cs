using System.Globalization;

using Equiv.Cli.Progress;
using Equiv.Core.Progress;

using Xunit;

namespace Equiv.Cli.Tests.Progress;

/// <summary>ADR 0038's line grammar (ticket M4-012): every field in its fixed place, in the invariant culture whatever the thread's culture.</summary>
public sealed class RunLogLineTests
{
    private static readonly TimeSpan At = new(1, 2, 3, 4, 567);

    [Fact]
    public void A_Full_Line_Has_Every_Field_In_Order()
    {
        RunLogLine line = new(At, "verify", 3, 42, 30, 400)
        {
            Item = "N.T::M(int)",
            Outcome = "equivalent",
            Took = TimeSpan.FromMilliseconds(1234.4),
            Eta = new TimeSpan(0, 1, 2, 3, 45),
            Worst = TimeSpan.FromHours(30),
            Rate = 2.26,
            Slow = true,
        };

        Assert.Equal(
            "equiv: +26:03:04 verify 3/42 (7%) item=N.T::M(int) outcome=equivalent took=1.234 eta=01:02:03.045 worst=30:00:00.000 rate=2.3/s slow",
            line.Format());
    }

    [Fact]
    public void A_Bare_Line_Has_Only_The_Counts_And_An_Unknown_Eta()
    {
        Assert.Equal("equiv: +00:00:00 load 0/1 (0%) eta=?", new RunLogLine(TimeSpan.Zero, "load", 0, 1, 0, 1).Format());
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(5, -1, 100)]
    [InlineData(1, 3, 33)]
    [InlineData(3, 3, 100)]
    public void Percent_Is_The_Share_Of_Weight_Done(long done, long total, long percent)
    {
        Assert.Equal(percent, new RunLogLine(TimeSpan.Zero, "verify", 0, 0, done, total).Percent);
    }

    [Fact]
    public void Phase_End_Names_Each_Checkpoint_And_The_Drops()
    {
        EtaEstimator estimator = new(100);
        _ = estimator.Observe(TimeSpan.FromSeconds(25), 25, 1);

        Assert.Equal(
            "equiv: +26:03:04 verify done in 00:00:40.500; eta@25%=00:01:15.000 eta@50%=? eta@75%=? dropped=12",
            RunLogLine.PhaseEnd(At, "verify", TimeSpan.FromMilliseconds(40_500), estimator, 12));
        Assert.Throws<ArgumentNullException>(() => RunLogLine.PhaseEnd(At, "verify", TimeSpan.Zero, null!, 0));
    }

    [Fact]
    public void Detail_Is_Prefixed_With_Its_Phase()
    {
        Assert.Equal("equiv: +26:03:04 verify detail: rung=direct took=0.001 result=unsat", RunLogLine.Detail(At, "verify", "rung=direct took=0.001 result=unsat"));
    }

    [Fact]
    public void Numbers_Use_The_Invariant_Culture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            RunLogLine line = new(At, "verify", 1234, 5678, 1, 2) { Took = TimeSpan.FromMilliseconds(1500), Rate = 1234.5 };

            Assert.Equal("equiv: +26:03:04 verify 1234/5678 (50%) took=1.500 eta=? rate=1234.5/s", line.Format());
            Assert.Equal("26:03:04.567", RunLogLine.Duration(At));
            Assert.Equal("+26:03:04", RunLogLine.Stamp(At));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
