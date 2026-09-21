using LicenceCheck;

using Xunit;

namespace LicenceCheck.Tests;

public sealed class NoticesCheckerTests
{
    [Fact]
    public void SkipsWhenSamplesWereNotFullyRestored()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: false,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: "anything",
            renderedNotices: "anything else",
            fix: false);

        Assert.Equal(NoticesOutcome.Skipped, decision.Outcome);
        Assert.Contains("samples/", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsUpToDateWhenContentMatches()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: true,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: "same",
            renderedNotices: "same",
            fix: false);

        Assert.Equal(NoticesOutcome.UpToDate, decision.Outcome);
        Assert.Null(decision.Message);
    }

    [Fact]
    public void IsUpToDateAcrossLineEndingDifferences()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: true,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: "line1\r\nline2",
            renderedNotices: "line1\nline2",
            fix: false);

        Assert.Equal(NoticesOutcome.UpToDate, decision.Outcome);
    }

    [Fact]
    public void RegeneratesWhenOutOfDateAndFixIsSet()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: true,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: "old",
            renderedNotices: "new",
            fix: true);

        Assert.Equal(NoticesOutcome.Regenerated, decision.Outcome);
        Assert.Contains("regenerated", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenOutOfDateAndFixIsNotSet()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: true,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: "old",
            renderedNotices: "new",
            fix: false);

        Assert.Equal(NoticesOutcome.OutOfDate, decision.Outcome);
        Assert.Contains("out of date", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TreatsAMissingFileAsOutOfDate()
    {
        NoticesDecision decision = NoticesChecker.Decide(
            samplesFullyRestored: true,
            repoRoot: "/repo",
            noticesPath: "/repo/THIRD-PARTY-NOTICES.md",
            existingNotices: null,
            renderedNotices: "new",
            fix: false);

        Assert.Equal(NoticesOutcome.OutOfDate, decision.Outcome);
    }
}
