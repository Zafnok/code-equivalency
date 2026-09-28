using System.Globalization;

using Equiv.Cli.Progress;
using Equiv.Core.Progress;

using Xunit;

namespace Equiv.Cli.Tests.Progress;

/// <summary>
/// <see cref="ChannelRunLog"/> on a <see cref="ManualTimeProvider"/> (ticket M4-012 criteria 3 to 5): the heartbeat
/// comes from the writer's own timer while the producer is stuck, a full channel drops and counts instead of blocking,
/// and each verbosity writes what ADR 0038 says to stderr and the <c>--log</c> file alike.
/// </summary>
public sealed class ChannelRunLogTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void Heartbeat_WhileProducerBlocked_NamesItem()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            log.Phase("verify", 2, 2, new PhaseBound(2, 5000, 1));
            log.Item("T::Stuck()", 1);
            error.WaitFor(1);

            time.Advance(Minute);
            error.WaitFor(2);
            time.Advance(Minute);
            IReadOnlyList<string> lines = error.WaitFor(3);

            Assert.Equal(
                [
                    "equiv: +00:00:00 verify 0/2 (0%) eta=? worst=00:00:10.000",
                    "equiv: +00:01:00 verify 0/2 (0%) item=T::Stuck() took=60.000 eta=? worst=00:00:10.000 rate=0.0/s",
                    "equiv: +00:02:00 verify 0/2 (0%) item=T::Stuck() took=120.000 eta=? worst=00:00:10.000 rate=0.0/s",
                ],
                lines);
        }
    }

    [Fact]
    public void Full_Channel_Drops_And_Counts()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            error.Hold();
            log.Phase("verify", ChannelRunLog.Capacity + 7, ChannelRunLog.Capacity + 7);
            error.WaitUntilBlocked();

            // The writer is stuck inside the phase's first line, so nothing leaves the channel while it fills.
            for (int i = 0; i < ChannelRunLog.Capacity + 7; i++)
            {
                log.Item("T::M()", 1);
            }

            Assert.Equal(ChannelRunLog.Capacity, log.Queued);
            error.Release();
            SpinWait.SpinUntil(() => log.Queued == 0, TimeSpan.FromSeconds(30));
            log.PhaseDone();
        }

        Assert.EndsWith(" dropped=7", error.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Quiet_Writes_Nothing()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        using LineWriter file = new();
        ChannelRunLog log = new(Verbosity.Quiet, error, file, time);
        using (log)
        {
            Assert.False(log.IsDebug);
            Run(log, time);
            time.Advance(Minute);
        }

        Assert.Empty(error.Lines);
        Assert.Empty(file.Lines);
    }

    [Fact]
    public void Log_File_Mirrors_Stderr()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        using LineWriter file = new();
        ChannelRunLog log = new(Verbosity.Debug, error, file, time);
        using (log)
        {
            Run(log, time);
        }

        Assert.NotEmpty(error.Lines);
        Assert.Equal(error.Lines, file.Lines);
    }

    [Fact]
    public void PhaseDone_Reports_Eta_Checkpoints()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            log.Phase("verify", 4, 100);
            for (int i = 0; i < 4; i++)
            {
                log.Item(string.Create(CultureInfo.InvariantCulture, $"T::M{i}()"), 25);
                time.Advance(TimeSpan.FromSeconds(10));
                log.ItemDone("equivalent");
            }

            log.PhaseDone();
        }

        Assert.Equal(
            [
                "equiv: +00:00:00 verify 0/4 (0%) eta=?",
                "equiv: +00:00:10 verify 1/4 (25%) eta=00:00:30.000 rate=0.1/s",
                "equiv: +00:00:20 verify 2/4 (50%) eta=00:00:20.000 rate=0.1/s",
                "equiv: +00:00:30 verify 3/4 (75%) eta=00:00:10.000 rate=0.1/s",
                "equiv: +00:00:40 verify 4/4 (100%) eta=00:00:00.000 rate=0.1/s",
                "equiv: +00:00:40 verify done in 00:00:40.000; eta@25%=00:00:30.000 eta@50%=00:00:20.000 eta@75%=00:00:10.000 dropped=0",
            ],
            error.Lines);
    }

    /// <summary>At <c>normal</c>, items between two 5% steps of the weight write nothing, and a detail is not even queued.</summary>
    [Fact]
    public void Normal_Writes_A_Line_Per_Five_Percent_Of_Weight()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            log.Phase("verify", 40, 40);
            for (int i = 0; i < 40; i++)
            {
                log.Item("T::M()", 1);
                log.Detail("never written");
                log.ItemDone("congruent");
            }

            log.PhaseDone();
        }

        Assert.Equal(1 + 20 + 1, error.Lines.Count);
        Assert.DoesNotContain(error.Lines, static l => l.Contains("detail", StringComparison.Ordinal));
        Assert.Equal("equiv: +00:00:00 verify 2/40 (5%) eta=00:00:00.000", error.Lines[1]);
    }

    [Fact]
    public void Debug_Writes_Every_Item_And_Detail_And_Beats_Every_Ten_Seconds()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Debug, error, file: null, time);
        using (log)
        {
            Assert.True(log.IsDebug);
            log.Phase("verify", 2, 30, new PhaseBound(1, 5000, 5));
            log.Item("T::A()", 10);
            log.Detail("rung=direct took=0.000 result=unsat");
            time.Advance(TimeSpan.FromSeconds(5));
            log.ItemDone("equivalent");
            log.Item("T::B()", 20);
            error.WaitFor(3);
            time.Advance(TimeSpan.FromSeconds(5));
            error.WaitFor(4);
            log.ItemDone("failed");
            log.PhaseDone();
        }

        Assert.Equal(
            [
                "equiv: +00:00:00 verify 0/2 (0%) eta=? worst=00:00:25.000",
                "equiv: +00:00:00 verify detail: rung=direct took=0.000 result=unsat",
                "equiv: +00:00:05 verify 1/2 (33%) item=T::A() outcome=equivalent took=5.000 eta=00:00:10.000 worst=00:00:25.000 rate=0.2/s",
                "equiv: +00:00:10 verify 1/2 (33%) item=T::B() took=5.000 eta=00:00:20.000 worst=00:00:25.000 rate=0.1/s",
                "equiv: +00:00:10 verify 2/2 (100%) item=T::B() outcome=failed took=5.000 eta=00:00:00.000 worst=00:00:00.000 rate=0.2/s",
                "equiv: +00:00:10 verify done in 00:00:10.000; eta@25%=00:00:10.000 eta@50%=00:00:00.000 eta@75%=00:00:00.000 dropped=0",
            ],
            error.Lines);
    }

    /// <summary>A heartbeat past ten times the phase's median item says <c>slow</c>; one within it does not.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(10, false)]
    public void Heartbeat_Says_Slow_Past_Ten_Times_The_Median(int firstSeconds, bool slow)
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            log.Phase("verify", 100, 100);
            log.Item("T::A()", 1);
            time.Advance(TimeSpan.FromSeconds(firstSeconds));
            log.ItemDone("equivalent");
            log.Item("T::B()", 1);
            SpinWait.SpinUntil(() => log.Queued == 0, TimeSpan.FromSeconds(30));
            error.WaitFor(1);
            time.Advance(Minute - TimeSpan.FromSeconds(firstSeconds));
            IReadOnlyList<string> lines = error.WaitFor(2);

            Assert.Equal(slow, lines[^1].EndsWith(" slow", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Heartbeat_Between_Items_Has_No_Item_And_Outside_A_Phase_Writes_Nothing()
    {
        ManualTimeProvider time = new();
        using LineWriter error = new();
        ChannelRunLog log = new(Verbosity.Normal, error, file: null, time);
        using (log)
        {
            time.Advance(Minute);
            log.Phase("write", 1, 0);
            error.WaitFor(1);
            time.Advance(Minute);
            error.WaitFor(2);
            log.Item("equiv.sarif", 0);
            log.ItemDone("written");
            log.PhaseDone();
        }

        Assert.Equal(
            [
                "equiv: +00:01:00 write 0/1 (100%) eta=?",
                "equiv: +00:02:00 write 0/1 (100%) eta=? rate=0.0/s",
                "equiv: +00:02:00 write 1/1 (100%) eta=? rate=0.0/s",
                "equiv: +00:02:00 write done in 00:01:00.000; eta@25%=? eta@50%=? eta@75%=? dropped=0",
            ],
            error.Lines);
    }

    [Fact]
    public void Dispose_Gives_Up_On_A_Stuck_Writer()
    {
        using LineWriter error = new();
        using ChannelRunLog log = new(Verbosity.Normal, error, file: null, new ManualTimeProvider());
        error.Hold();
        log.Phase("load", 1, 1);
        error.WaitUntilBlocked();

        log.Dispose();

        Assert.Empty(error.Lines);
        error.Release();
    }

    [Fact]
    public void Constructor_Rejects_Nulls()
    {
        using LineWriter error = new();

        Assert.Throws<ArgumentNullException>(() => new ChannelRunLog(Verbosity.Normal, null!, file: null, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => new ChannelRunLog(Verbosity.Normal, error, file: null, null!));
    }

    /// <summary>One phase with a detail, two items and a heartbeat.</summary>
    private static void Run(ChannelRunLog log, ManualTimeProvider time)
    {
        log.Phase("verify", 2, 2);
        log.Item("T::A()", 1);
        log.Detail("detail");
        time.Advance(TimeSpan.FromSeconds(1));
        log.ItemDone("equivalent");
        log.Item("T::B()", 1);
        log.ItemDone("divergent");
        log.PhaseDone();
    }
}
