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
public sealed class EndpointDiscoverySampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    [Fact]
    public void MatchesTheActionsByEndpointRouteAlone()
    {
        string legacy = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "legacy"), "*.sln").Single();
        string modern = Directory.GetFiles(Path.Combine(SamplesRoot, "webapi-basic", "modern"), "*.slnx").Single();

        MatchResult result = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, TestContext.Current.CancellationToken);

        ProcedurePair pair = Assert.Single(result.Pairs);
        Assert.Equal("GET /api/orders/{id}", pair.Old.Value);
        Assert.Equal("GET /api/orders/{id}", pair.New.Value);
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

        MatchResult result = new CSharpFrontend().Analyze(legacy, modern, EquivConfig.Default, TestContext.Current.CancellationToken);
        ProcedurePair pair = Assert.Single(result.Pairs);

        string dump = $"""
            legacy:
            {IrText.Dump(pair.OldBody!)}

            modern:
            {IrText.Dump(pair.NewBody!)}
            """;
        return VerifyXunit.Verifier.Verify(dump);
    }
}
