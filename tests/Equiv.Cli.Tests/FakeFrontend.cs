using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;
using Equiv.Core.Matching;
using Equiv.Core.Progress;

namespace Equiv.Cli.Tests;

/// <summary>
/// A frontend with a configurable <see cref="Supports"/> predicate and a canned <see cref="Analyze"/> result or exception.
/// Without a canned <see cref="MatchResult"/> it matches nothing; without canned <see cref="AnalysedLines"/> it counts 0 on both sides.
/// <paramref name="replay"/> is the analysis's replay factory (ticket M4-009); without one it cannot replay.
/// <paramref name="legacyRuntimes"/> and <paramref name="modernRuntimes"/> are each side's project runtimes (ticket P2-053).
/// <paramref name="generatedProcedures"/> is each side's count of procedures a source generator emitted (ticket P2-118).
/// <paramref name="replayUnder"/>, when given, picks the replay factory from the config <see cref="Analyze"/> was given, as a
/// real frontend's runtimes depend on the config (ticket P2-241); it overrides <paramref name="replay"/>.
/// </summary>
internal sealed class FakeFrontend(
    string language,
    Func<string, bool> supports,
    MatchResult? matchResult = null,
    FrontendLoadException? throwOnAnalyze = null,
    AnalysedLines? lines = null,
    string[]? legacyNotBuilt = null,
    IReplayDriverFactory? replay = null,
    (string Project, string Runtime, string Source)[]? legacyRuntimes = null,
    (string Project, string Runtime, string Source)[]? modernRuntimes = null,
    (int Legacy, int Modern) generatedProcedures = default,
    Func<EquivConfig, IReplayDriverFactory?>? replayUnder = null) : ILanguageFrontend
{
    public int AnalyzeCallCount { get; private set; }

    /// <summary>The config the last <see cref="Analyze"/> was given.</summary>
    public EquivConfig? LastConfig { get; private set; }

    public string Language { get; } = language;

    public bool Supports(string path) => supports(path);

    public FrontendAnalysis Analyze(string legacyPath, string modernPath, EquivConfig config, IRunLog log, CancellationToken ct)
    {
        AnalyzeCallCount++;
        LastConfig = config;
        return throwOnAnalyze is not null
            ? throw throwOnAnalyze
            : new FrontendAnalysis(matchResult ?? new MatchResult([], [], [], []), lines ?? new AnalysedLines(0, 0))
            {
                LegacyNotBuilt = [.. legacyNotBuilt ?? []],
                LegacyRuntimes = [.. legacyRuntimes ?? []],
                ModernRuntimes = [.. modernRuntimes ?? []],
                GeneratedProcedures = generatedProcedures,
                Replay = replayUnder is null ? replay : replayUnder(config),
            };
    }
}
