namespace Equiv.Cli;

/// <summary>
/// The already-parsed <c>equiv compare</c> options, bundled so <see cref="CompareCommand.Run"/> takes one options parameter plus its collaborators (sonar(src): GH-40, csharpsquid:S107).
/// <see cref="FailOn"/> is null when <c>--fail-on</c> was not given, which means <c>divergent</c> and lets <c>--lower-only</c> reject only an explicit one.
/// <see cref="Execute"/> is <c>--execute</c>: replay every Divergent on both real runtimes (ADR 0035; ticket M4-009).
/// </summary>
internal sealed record CompareOptions(
    string LegacyPath,
    string ModernPath,
    string OutPath,
    string? BaselinePath,
    string? ConfigPath,
    string? FailOn,
    bool DryRun,
    bool LowerOnly = false,
    bool Execute = false);
