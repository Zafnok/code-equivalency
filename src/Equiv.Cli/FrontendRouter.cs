using Equiv.Core;

namespace Equiv.Cli;

/// <summary>
/// Picks the single configured <see cref="ILanguageFrontend"/> that supports both input paths
/// (ARCHITECTURE.md's "Equiv.Cli" router bullet). <c>null</c> means no frontend supports both —
/// either none supports either path, or two different frontends each support only one side.
/// </summary>
internal static class FrontendRouter
{
    public static ILanguageFrontend? Route(IReadOnlyList<ILanguageFrontend> frontends, string legacyPath, string modernPath)
    {
        ArgumentNullException.ThrowIfNull(frontends);

        foreach (ILanguageFrontend frontend in frontends)
        {
            if (frontend.Supports(legacyPath) && frontend.Supports(modernPath))
            {
                return frontend;
            }
        }

        return null;
    }
}
