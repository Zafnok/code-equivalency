using Equiv.Core.Configuration;
using Equiv.Core.Matching;

using Xunit;

namespace Equiv.Core.Tests;

/// <summary>The interface itself has no behaviour; this pins the stub signature <c>Equiv.Cli.FrontendRouter</c> and <c>Equiv.Frontend.CSharp</c> build against.</summary>
public sealed class ILanguageFrontendTests
{
    private sealed class AlwaysSupports : ILanguageFrontend
    {
        public string Language => "stub";

        public bool Supports(string path) => true;

        public MatchResult Analyze(string legacyPath, string modernPath, EquivConfig config, CancellationToken ct) =>
            new([], [], [], []);
    }

    [Fact]
    public void ImplementationsCanBeInvokedThroughTheInterface()
    {
        ILanguageFrontend frontend = new AlwaysSupports();

        Assert.Equal("stub", frontend.Language);
        Assert.True(frontend.Supports("anything"));
        Assert.Equal(new MatchResult([], [], [], []), frontend.Analyze("a", "b", EquivConfig.Default, CancellationToken.None));
    }
}
