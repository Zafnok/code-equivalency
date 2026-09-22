using Equiv.Core;

using Xunit;

namespace Equiv.Cli.Tests;

/// <summary>Picks the single frontend that supports both input paths (ARCHITECTURE.md's "Equiv.Cli" router bullet).</summary>
public sealed class FrontendRouterTests
{
    [Fact]
    public void Route_RejectsNullFrontends()
    {
        Assert.Throws<ArgumentNullException>(static () => FrontendRouter.Route(null!, "a", "b"));
    }

    [Fact]
    public void Route_ReturnsNullWhenNoFrontendSupportsBothPaths()
    {
        FakeFrontend frontend = new("stub", static _ => false);

        ILanguageFrontend? result = FrontendRouter.Route([frontend], "legacy.cs", "modern.cs");

        Assert.Null(result);
    }

    [Fact]
    public void Route_ReturnsTheFrontendThatSupportsBothPaths()
    {
        FakeFrontend frontend = new("stub", static path => path is "legacy.cs" or "modern.cs");

        ILanguageFrontend? result = FrontendRouter.Route([frontend], "legacy.cs", "modern.cs");

        Assert.Same(frontend, result);
    }

    [Fact]
    public void Route_ReturnsNullWhenEachPathIsSupportedByADifferentFrontend()
    {
        FakeFrontend legacyOnly = new("legacy", static path => path.Equals("legacy.cs", StringComparison.Ordinal));
        FakeFrontend modernOnly = new("modern", static path => path.Equals("modern.cs", StringComparison.Ordinal));

        ILanguageFrontend? result = FrontendRouter.Route([legacyOnly, modernOnly], "legacy.cs", "modern.cs");

        Assert.Null(result);
    }
}
