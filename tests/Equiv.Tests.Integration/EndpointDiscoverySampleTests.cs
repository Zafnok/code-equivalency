using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// M2-005 acceptance criterion 5 against the real sample: the legacy Web API 2 action and the modern
/// ASP.NET Core action have different C# identities (different base class, different attribute types,
/// different namespace-qualified attributes) but the same real <c>Microsoft.AspNet.WebApi.Core</c>/
/// <c>Microsoft.AspNetCore.Mvc.Core</c> route attributes normalise to the same endpoint identity, so
/// <see cref="CSharpFrontend.Analyze"/> matches them on that identity alone.
/// </summary>
[Trait("Category", "Integration")]
// Both classes load samples/webapi-basic; MSBuild's design-time build of one sample writes the same obj/ state
// file, so two concurrent loads collide ("Could not write state file ... AssemblyReference.cache").
[Collection("WebApiBasicSample")]
public sealed class EndpointDiscoverySampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void MatchesTheActionsByEndpointRouteAlone()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "modern"), "*.slnx").Single();

        MatchResult result = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, TestContext.Current.CancellationToken).Match;

        // Find (ticket M3-009) is the sample's second action.
        Assert.Equal(["GET /api/orders/find/{id}", "GET /api/orders/{id}"], result.Pairs.Select(static p => p.New.Value).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        ProcedurePair pair = result.Pairs.Single(static p => string.Equals(p.New.Value, "GET /api/orders/{id}", StringComparison.Ordinal));
        Assert.Equal("GET /api/orders/{id}", pair.Old.Value);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Empty(result.Ambiguous);
        Assert.Empty(IrValidator.Validate(pair.OldBody!));
        Assert.Empty(IrValidator.Validate(pair.NewBody!));
    }

    [Fact]
    public Task MatchedPairLowersToTheSameIr()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "modern"), "*.slnx").Single();

        MatchResult result = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        ProcedurePair pair = result.Pairs.Single(static p => string.Equals(p.New.Value, "GET /api/orders/{id}", StringComparison.Ordinal));

        string dump = $"""
            legacy:
            {IrText.Dump(pair.OldBody!)}

            modern:
            {IrText.Dump(pair.NewBody!)}
            """;
        return VerifyXunit.Verifier.Verify(dump);
    }
}
