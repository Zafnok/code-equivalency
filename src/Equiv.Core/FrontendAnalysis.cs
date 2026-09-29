using System.Collections.Immutable;

using Equiv.Core.Execution;
using Equiv.Core.Matching;

namespace Equiv.Core;

/// <summary>
/// What <see cref="ILanguageFrontend.Analyze"/> returns: the matched, lowered procedures and the analysed line
/// count of each side (ticket M3-014), both taken from the one load of each solution. <see cref="LegacyNotBuilt"/> and
/// <see cref="ModernNotBuilt"/> name each side's projects that its solution does not build, which were not loaded
/// (P2-013). <see cref="LegacyRuntimes"/> and <see cref="ModernRuntimes"/> give each side's loaded projects their runtime and
/// where it came from: <c>attribute</c>, <c>host</c>, <c>config</c> or <c>unhosted</c> (ADR 0040; P2-053). <see cref="Replay"/> builds replay drivers from the same loaded projects for <c>--execute</c> (ticket M4-009),
/// and is null for a frontend that cannot.
/// </summary>
public sealed record FrontendAnalysis(MatchResult Match, AnalysedLines Lines)
{
    public ImmutableArray<string> LegacyNotBuilt { get; init; } = [];

    public ImmutableArray<string> ModernNotBuilt { get; init; } = [];

    public ImmutableArray<(string Project, string Runtime, string Source)> LegacyRuntimes { get; init; } = [];

    public ImmutableArray<(string Project, string Runtime, string Source)> ModernRuntimes { get; init; } = [];

    public IReplayDriverFactory? Replay { get; init; }
}
