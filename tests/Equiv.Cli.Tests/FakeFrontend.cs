using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;
using Equiv.Core.Matching;

namespace Equiv.Cli.Tests;

/// <summary>
/// A frontend with a configurable <see cref="Supports"/> predicate and a canned <see cref="Analyze"/> result or exception.
/// Without a canned <see cref="MatchResult"/> it matches nothing; without canned <see cref="AnalysedLines"/> it counts 0 on both sides.
/// <paramref name="replay"/> is the analysis's replay factory (ticket M4-009); without one it cannot replay.
/// </summary>
internal sealed class FakeFrontend(
    string language,
    Func<string, bool> supports,
    MatchResult? matchResult = null,
    FrontendLoadException? throwOnAnalyze = null,
    AnalysedLines? lines = null,
    string[]? legacyNotBuilt = null,
    IReplayDriverFactory? replay = null) : ILanguageFrontend
{
    public int AnalyzeCallCount { get; private set; }

    public string Language { get; } = language;

    public bool Supports(string path) => supports(path);

    public FrontendAnalysis Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct)
    {
        AnalyzeCallCount++;
        return throwOnAnalyze is not null
            ? throw throwOnAnalyze
            : new FrontendAnalysis(matchResult ?? new MatchResult([], [], [], []), lines ?? new AnalysedLines(0, 0)) { LegacyNotBuilt = [.. legacyNotBuilt ?? []], Replay = replay };
    }
}
