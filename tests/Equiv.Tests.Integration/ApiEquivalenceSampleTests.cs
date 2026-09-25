using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-009 acceptance criteria 6 and 7 against the real samples: <c>webapi-basic</c>'s <c>Find</c> and
/// <c>api-drift</c>'s <c>Parts</c> and <c>HasX</c> bind different members on .NET Framework 4.8 and .NET 10, the legacy
/// side is rewritten by the shipped catalogue, and each pair lists the entries applied to it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ApiEquivalenceSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> SampleNames => ["webapi-basic", "api-drift"];

    [Theory]
    [MemberData(nameof(SampleNames))]
    public void Samples_LowerWithoutOpaque(string sample)
    {
        MatchResult result = Analyze(sample);

        Assert.NotEmpty(result.Pairs);
        foreach (IrProcedure body in result.Pairs.SelectMany(static p => new[] { p.OldBody!, p.NewBody! }))
        {
            Assert.Empty(IrValidator.Validate(body));
            Assert.Empty(body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Select(o => $"{body.Identity.Value}: {o.Reason}"));
        }
    }

    [Fact]
    public void WebApiBasic_FindListsTheWebApiEntries()
    {
        ProcedurePair find = Pair(Analyze("webapi-basic"), "GET /api/orders/find/{id}");

        Assert.Equal(
            ["webapi.not-found", "webapi.ok-of-int", "webapi.type.action-result", "webapi.type.not-found-result", "webapi.type.ok-content-result"],
            find.EquivalencesApplied);
        Assert.Empty(Pair(Analyze("webapi-basic"), "GET /api/orders/{id}").EquivalencesApplied);
    }

    [Fact]
    public void ApiDrift_EachMethodListsItsEntry()
    {
        MatchResult result = Analyze("api-drift");

        Assert.Equal(["bcl.string-split-one-char"], Pair(result, "Equiv.Samples.ApiDrift.Text::Parts(string)").EquivalencesApplied);
        Assert.Equal(["bcl.string-contains-char"], Pair(result, "Equiv.Samples.ApiDrift.Text::HasX(string)").EquivalencesApplied);
    }

    [Theory]
    [MemberData(nameof(SampleNames))]
    public Task SampleLoweringSnapshot(string sample)
    {
        MatchResult result = Analyze(sample);
        string dump = string.Join(
            "\n\n",
            result.Pairs.OrderBy(static p => p.New.Value, StringComparer.Ordinal).Select(static p => $"""
                {p.New.Value} [{string.Join(", ", p.EquivalencesApplied)}]
                legacy:
                {IrText.Dump(p.OldBody!)}
                modern:
                {IrText.Dump(p.NewBody!)}
                """));
        return VerifyXunit.Verifier.Verify(dump).UseParameters(sample);
    }

    private static ProcedurePair Pair(MatchResult result, string identity) =>
        result.Pairs.Single(p => string.Equals(p.New.Value, identity, StringComparison.Ordinal));

    private static MatchResult Analyze(string sample) =>
        new CSharpFrontend().Analyze(
            Directory.GetFiles(Path.Combine(SamplesRoot, sample, "legacy"), "*.sln").Single(),
            Directory.GetFiles(Path.Combine(SamplesRoot, sample, "modern"), "*.slnx").Single(),
            EquivConfig.Default,
            TestContext.Current.CancellationToken).Match;
}
