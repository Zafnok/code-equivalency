using Equiv.Cli;
using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Verify.Z3;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M3-015 on the real samples: congruence never contradicts the solver on any sample pair (acceptance criterion 7).
/// </summary>
[Trait("Category", "Integration")]
[Collection("Console")]
public sealed class CongruenceSampleTests
{
    private static string SamplesRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    public static TheoryData<string> Samples =>
        [.. Directory.GetDirectories(SamplesRoot).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Samples))]
    public void CongruenceNeverContradictsTheSolverOnASample(string sample)
    {
        MatchResult result = new CSharpFrontend().Analyze(Solution(sample, "legacy"), Solution(sample, "modern"), EquivConfig.Default, TestContext.Current.CancellationToken).Match;
        VerificationOptions options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, EquivConfig.Default.CallIdentityRenames);

        foreach (ProcedurePair pair in result.Pairs.Where(static p => CompareCommand.IsCongruent(p, p.OldBody!, p.NewBody!)))
        {
            Assert.False(new Z3Backend().Verify(pair.OldBody!, pair.NewBody!, options) is Divergent, $"{pair.New.Value} is congruent but Divergent");
        }
    }

    private static string Solution(string sample, string side) =>
        Directory.GetFiles(Path.Combine(SamplesRoot, sample, side), string.Equals(side, "legacy", StringComparison.Ordinal) ? "*.sln" : "*.slnx").Single();
}
