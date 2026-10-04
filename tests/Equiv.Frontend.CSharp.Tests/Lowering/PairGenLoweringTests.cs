using CsCheck;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Corpus.Seeder;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket M0-012: <see cref="PairGen"/> generates only constructs IOPERATION-COVERAGE.md marks lowered, so the
/// differential soundness gate tests verdicts rather than <c>IrOpaque</c> nodes. Both sides of every generated pair
/// compile and lower to valid IR with no opaque node.
/// </summary>
public sealed class PairGenLoweringTests
{
    private const int Pairs = 300;

    private const int IlPairs = 100;

    private const string Seed = "000000000000";

    [Fact]
    public void EveryGeneratedSideLowersWithoutOpaque() =>
        PairGen.Pair.Sample(
            static pair =>
            {
                foreach (string source in (string[])[pair.LegacySource, pair.ModernSource])
                {
                    Compilation compilation = RoslynTestCompilations.Compile(source);
                    Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
                    IMethodSymbol method = compilation.GetTypeByMetadataName("Oracle")!.GetMembers("M").OfType<IMethodSymbol>().Single();
                    IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);
                    Assert.Empty(IrValidator.Validate(procedure));
                    // Ticket P2-086: the interpolated string of a ConcatToInterpolation pair (ticket P2-048) holds only
                    // string holes, so it lowers as the concatenation it replaced.
                    Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions), static i => i is IrOpaque);
                }
            },
            seed: Seed,
            iter: Pairs,
            print: static pair => $"{pair.Operator}\n{pair.LegacySource}\n{pair.ModernSource}");

    /// <summary>
    /// Ticket P1-017: both sides of every pair that holds an IL fallback construct compile and lower from IL to valid IR,
    /// which is what the differential gate's IL mode verifies.
    /// </summary>
    [Fact]
    public void EveryIlPairSideLowersFromIl() =>
        PairGen.IlPair.Sample(
            static pair =>
            {
                foreach (string source in (string[])[pair.LegacySource, pair.ModernSource])
                {
                    Compilation compilation = RoslynTestCompilations.Compile(source);
                    Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
                    IMethodSymbol method = compilation.GetTypeByMetadataName("Oracle")!.GetMembers("M").OfType<IMethodSymbol>().Single();
                    Assert.Empty(IrValidator.Validate(IlLowerer.Lower(method, compilation, Runtimes.Migration)));
                }
            },
            seed: Seed,
            iter: IlPairs,
            print: static pair => $"{pair.Operator}\n{pair.LegacySource}\n{pair.ModernSource}");

    /// <summary>
    /// Ticket P2-079: both sides of every pair that differs only inside a lambda or a local function lower from IL to valid
    /// IR, and no opaque that stands for the lambda's pointer or the local function's call has a fingerprint, so the other
    /// side cannot share it.
    /// </summary>
    [Fact]
    public void NoClosurePairSideSharesItsClosure() =>
        PairGen.ClosurePair.Sample(
            static pair =>
            {
                foreach (string source in (string[])[pair.LegacySource, pair.ModernSource])
                {
                    Compilation compilation = RoslynTestCompilations.Compile(source);
                    Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
                    IMethodSymbol method = compilation.GetTypeByMetadataName("Oracle")!.GetMembers("M").OfType<IMethodSymbol>().Single();
                    IrProcedure procedure = IlLowerer.Lower(method, compilation, Runtimes.Migration);
                    Assert.Empty(IrValidator.Validate(procedure));
                    Assert.DoesNotContain(
                        procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>(),
                        static o => o.Fingerprint is not null && (o.Reason.Contains("[lambda]", StringComparison.Ordinal) || o.Reason.Contains("[local function]", StringComparison.Ordinal)));
                }
            },
            seed: Seed,
            iter: IlPairs,
            print: static pair => $"{pair.Operator}\n{pair.LegacySource}\n{pair.ModernSource}");

    /// <summary>Every input renders as the arguments a failure prints.</summary>
    [Fact]
    public void EveryInputPrintsEachArgument() =>
        PairGen.Input.Sample(
            static input => Assert.Matches(@"^a=-?\d+ b=-?\d+ c=-?\d+ d=-?\d+ e=(True|False) s=(null|""s"") F=-?\d+ u=(null|\[(-?\d+(,-?\d+)*)?\])$", input.ToString()),
            seed: Seed,
            iter: Pairs);
}
