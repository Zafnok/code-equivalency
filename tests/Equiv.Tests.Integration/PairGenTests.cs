using CsCheck;

using Equiv.Corpus.Seeder;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>Ticket M0-012: <see cref="PairGen"/> draws every operator, and both sides of every pair compile.</summary>
public sealed class PairGenTests
{
    private const int Draws = 2_000;

    private const int Compiled = 300;

    /// <summary>Acceptance criterion 5.</summary>
    [Fact]
    public void EveryOperatorIsDrawn() =>
        PairGen.Pair.Array[Draws].Sample(
            static pairs => Assert.Equal(Enum.GetValues<MutationOperator>(), pairs.Select(static p => p.Operator).Distinct().Order()),
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: 1,
            print: static pairs => $"{pairs.Length} pairs");

    [Fact]
    public void PreservingOperatorsCompile() => Compile(preserving: true);

    [Fact]
    public void ChangingOperatorsCompile() => Compile(preserving: false);

    private static void Compile(bool preserving) =>
        PairGen.Pair.Where(p => PairGen.IsPreserving(p.Operator) == preserving).Sample(
            static pair =>
            {
                foreach (string source in (string[])[pair.LegacySource, pair.ModernSource])
                {
                    Diagnostic[] errors = [.. PairRuntime.Compile(source, "Pair").GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error)];
                    Assert.True(errors.Length == 0, $"{pair.Operator}: {string.Join('\n', errors.Select(static e => e.ToString()))}\n{source}");
                }
            },
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: Compiled);
}
