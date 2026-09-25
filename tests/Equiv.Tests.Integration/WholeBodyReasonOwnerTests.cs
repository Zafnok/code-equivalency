using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Frontend.CSharp;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-025 criterion 7 (ADR 0029 decision 3): every whole-body opaque the frontend gives any sample has an owner in
/// <see cref="WholeBodyReasonOwners"/>. <c>business-layer</c>'s census snapshot (<see cref="LoweringCensusTests"/>) is the
/// ratchet that keeps their number from rising.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WholeBodyReasonOwnerTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public void EveryWholeBodyReasonHasAnOwner(string sample)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;

        string[] unowned =
        [
            .. result.Pairs
                .SelectMany(static p => new[] { p.OldBody, p.NewBody })
                .OfType<IrProcedure>()
                .SelectMany(static body => body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Where(static o => o.WholeBody).Select(o => (body, o.Reason)))
                .Where(static e => !WholeBodyReasonOwners.Table.ContainsKey(e.Reason))
                .Select(static e => $"{e.body.Identity.Value}: {e.Reason}"),
        ];

        Assert.Empty(unowned);
    }

    [Fact]
    public void EveryOwnerIsATicketId() =>
        Assert.All(WholeBodyReasonOwners.Table.Values, static owner => Assert.Matches(@"^[MP]\d-\d{3}$", owner));

    private static string Solution(string sample, string side) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, side), string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();
}
