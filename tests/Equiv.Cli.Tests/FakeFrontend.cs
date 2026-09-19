using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;

namespace Equiv.Cli.Tests;

/// <summary>A frontend with a configurable <see cref="Supports"/> predicate and a canned <see cref="Analyze"/> result or exception.</summary>
internal sealed class FakeFrontend(string language, Func<string, bool> supports, MatchResult? matchResult = null, FrontendLoadException? throwOnAnalyze = null) : ILanguageFrontend
{
    public int AnalyzeCallCount { get; private set; }

    public string Language { get; } = language;

    public bool Supports(string path) => supports(path);

    public MatchResult Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct)
    {
        AnalyzeCallCount++;
        return throwOnAnalyze is not null ? throw throwOnAnalyze : matchResult!;
    }
}
