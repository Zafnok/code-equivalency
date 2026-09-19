using Equiv.Core.Configuration;
using Equiv.Core.Matching;

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
    /// Loads both sides, lowers them to IR, and matches procedures across them. Throws
    /// <see cref="FrontendLoadException"/> when a path cannot be loaded enough to attempt lowering.
    /// </summary>
    MatchResult Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct);
}
