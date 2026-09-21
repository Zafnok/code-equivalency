namespace LicenceCheck;

internal enum NoticesOutcome
{
    /// <summary>samples/ was not restored in this run; the resolved set is a known-partial subset, so no comparison was made.</summary>
    Skipped,
    UpToDate,
    Regenerated,
    OutOfDate,
}
