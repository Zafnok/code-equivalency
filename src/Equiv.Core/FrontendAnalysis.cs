using Equiv.Core.Matching;

namespace Equiv.Core;

/// <summary>
/// What <see cref="ILanguageFrontend.Analyze"/> returns: the matched, lowered procedures and the analysed line
/// count of each side (ticket M3-014), both taken from the one load of each solution.
/// </summary>
public sealed record FrontendAnalysis(MatchResult Match, AnalysedLines Lines);
