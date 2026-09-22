namespace Equiv.Cli;

/// <summary>The already-parsed <c>equiv compare</c> options, bundled so <see cref="CompareCommand.Run"/> takes one options parameter plus its collaborators (sonar(src): GH-40, csharpsquid:S107).</summary>
internal sealed record CompareOptions(
    string LegacyPath,
    string ModernPath,
    string OutPath,
    string? BaselinePath,
    string? ConfigPath,
    string FailOn,
    bool DryRun);
