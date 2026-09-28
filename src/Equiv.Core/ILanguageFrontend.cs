using Equiv.Core.Configuration;
using Equiv.Core.Progress;

namespace Equiv.Core;

/// <summary>
/// A language-specific pipeline (ARCHITECTURE.md's extension-points table): decides whether it can
/// handle a given path, then loads, lowers, and matches both sides into a <see cref="MatchResult"/>.
/// <c>Equiv.Frontend.CSharp</c> implements this against Roslyn; <c>Equiv.Cli.FrontendRouter</c>
/// selects among the configured frontends by <see cref="Supports"/>.
/// </summary>
public interface ILanguageFrontend
{
    /// <summary>The frontend's display name, e.g. <c>"csharp"</c>.</summary>
    string Language { get; }

    /// <summary>Whether this frontend can load and analyse <paramref name="path"/>.</summary>
    bool Supports(string path);

    /// <summary>
    /// Loads both sides, lowers them to IR, matches procedures across them, and counts each side's analysed
    /// lines, reporting the <c>load-legacy</c>, <c>load-modern</c>, <c>enumerate</c>, <c>match</c> and <c>lower</c>
    /// phases to <paramref name="log"/> (ADR 0038; ticket M4-013). Throws <see cref="FrontendLoadException"/> when a path cannot be loaded enough to attempt lowering.
    /// </summary>
    FrontendAnalysis Analyze(string legacyPath, string modernPath, EquivConfig config, IRunLog log, CancellationToken ct);
}
