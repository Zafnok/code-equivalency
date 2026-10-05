using Equiv.Cli.Progress;
using Equiv.Execute.Testing;

namespace Equiv.Cli;

/// <summary>
/// The already-parsed <c>equiv compare</c> options, bundled so <see cref="CompareCommand.Run"/> takes one options parameter plus its collaborators (sonar(src): GH-40, csharpsquid:S107).
/// <see cref="FailOn"/> is null when <c>--fail-on</c> was not given, which means <c>divergent</c> and lets <c>--lower-only</c> reject only an explicit one.
/// <see cref="Execute"/> is <c>--execute</c>: replay every Divergent on both real runtimes (ADR 0035; ticket M4-009).
/// <see cref="ChcIntMode"/> is <c>--chc-int-mode</c>, on unless given as false (<see cref="Core.VerificationOptions.ChcIntMode"/>; ticket P1-001).
/// <see cref="InvariantModel"/> is <c>--invariant-model</c>: the model rung 5 asks for a coupling invariant when rung 4
/// times out, off when null (<see cref="Core.VerificationOptions.InvariantModel"/>; ticket P1-002).
/// <see cref="Testing"/> is <c>--test-target</c> and <c>--test-budget</c>, which bound testing each Unknown pair under
/// <c>--execute</c> and do nothing without it (ADR 0035 decision 3; ticket P1-008).
/// <see cref="Verbosity"/> and <see cref="LogPath"/> are <c>--verbosity</c> and <c>--log</c>: how much progress the run
/// writes to stderr, and the file that mirrors it (ADR 0038; ticket M4-012).
/// <see cref="Streams"/> is where the run's stdout and stderr lines go, the console's when null; <c>equiv mcp</c> passes its own
/// because stdout is the protocol channel there (ADR 0033; ticket M5-001). <see cref="Bound"/> and <see cref="TimeoutMs"/>
/// override the config's values of the same name and must be positive; <c>equiv mcp</c>'s <c>compare</c> tool sets them.
/// <see cref="ResourceLimit"/> is <c>--resource-limit</c>, which overrides the config's <c>resourceLimit</c> the same way
/// (<see cref="Core.VerificationOptions.ResourceLimit"/>; ticket P2-050).
/// <see cref="Jobs"/> is <c>--jobs</c>, which overrides the config's <c>jobs</c> the same way: how many pairs are verified
/// at once, one by default (<see cref="PairWorkers"/>; ticket P2-077).
/// <see cref="IlFallback"/> is <c>--il-fallback</c>, off by default: lower a pair with an unshared opaque again from IL (ADR 0039;
/// ticket P1-016).
/// <see cref="Mode"/> is <c>--mode</c>, <c>thorough</c> or <c>quick</c>, which overrides the config's <c>mode</c>; null
/// leaves the config's, thorough unless set, and any other name is exit 3 (ADR 0049; ticket P1-032).
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
    bool Execute = false,
    bool ChcIntMode = true,
    string? InvariantModel = null)
{
    public TestingOptions Testing { get; init; } = TestingOptions.Default;

    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

    public string? LogPath { get; init; }

    public Streams? Streams { get; init; }

    public int? Bound { get; init; }

    public int? TimeoutMs { get; init; }

    public int? ResourceLimit { get; init; }

    public int? Jobs { get; init; }

    public bool IlFallback { get; init; }

    public string? Mode { get; init; }
}
