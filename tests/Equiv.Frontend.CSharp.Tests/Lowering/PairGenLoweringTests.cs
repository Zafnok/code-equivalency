using CsCheck;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
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
                    IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, []);
                    Assert.Empty(IrValidator.Validate(procedure));
                    Assert.Empty(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>());
                }
            },
            seed: Seed,
            iter: Pairs,
            print: static pair => $"{pair.Operator}\n{pair.LegacySource}\n{pair.ModernSource}");

    /// <summary>Every input renders as the arguments a failure prints.</summary>
    [Fact]
    public void EveryInputPrintsEachArgument() =>
        PairGen.Input.Sample(
            static input => Assert.Matches(@"^a=-?\d+ b=-?\d+ c=-?\d+ d=-?\d+ e=(True|False) s=(null|""s"") F=-?\d+ u=(null|\[(-?\d+(,-?\d+)*)?\])$", input.ToString()),
            seed: Seed,
            iter: Pairs);
}
