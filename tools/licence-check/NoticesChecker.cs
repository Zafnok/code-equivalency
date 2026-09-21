namespace LicenceCheck;

/// <summary>
/// Pure decision: what to do about THIRD-PARTY-NOTICES.md, given what is already on disk and what
/// the renderer produced for this run. No file or console I/O, so it is exercised directly by tests
/// rather than only through a real ./build.ps1 run (ticket M0-010, AC6).
/// </summary>
internal static class NoticesChecker
{
    public static NoticesDecision Decide(bool samplesFullyRestored, string repoRoot, string noticesPath, string? existingNotices, string renderedNotices, bool fix)
    {
        if (!samplesFullyRestored)
        {
            // samples/ is only restored under build.ps1 -Integration (the legacy side needs
            // MSBuild.exe, Windows-only). This run's resolved package set is a real subset of the
            // truth, so comparing it against THIRD-PARTY-NOTICES.md would report false drift (or,
            // with --fix, overwrite the tracked file with an incomplete one).
            return new NoticesDecision(NoticesOutcome.Skipped, $"licence-check: samples/ was not restored under '{repoRoot}' in this run; skipping the THIRD-PARTY-NOTICES.md freshness check.");
        }

        if (string.Equals(Normalize(existingNotices), Normalize(renderedNotices), StringComparison.Ordinal))
        {
            return new NoticesDecision(NoticesOutcome.UpToDate, Message: null);
        }

        return fix
            ? new NoticesDecision(NoticesOutcome.Regenerated, $"licence-check: regenerated {noticesPath}.")
            : new NoticesDecision(NoticesOutcome.OutOfDate, $"licence-check: {noticesPath} is out of date. Run with --fix to regenerate it.");
    }

    private static string Normalize(string? text) => (text ?? string.Empty).ReplaceLineEndings("\n");
}
