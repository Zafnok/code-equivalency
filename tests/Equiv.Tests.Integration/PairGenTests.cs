using CsCheck;

using Equiv.Corpus.Seeder;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket M0-012: <see cref="PairGen"/> draws every operator, and both sides of every pair compile. Ticket P1-017: the pairs
/// the differential gate draws hold the constructs the IL fallback exists for, in both families.
/// </summary>
public sealed class PairGenTests
{
    private const int Draws = 2_000;

    private const int Compiled = 300;

    /// <summary>Pairs drawn to find every IL fallback construct in both families.</summary>
    private const int IlDraws = 400;

    private static readonly string[] IlConstructs = ["lifted operator", "nullable conversion", "interpolated string", "positional pattern in a switch"];

    /// <summary>Acceptance criterion 5.</summary>
    [Fact]
    public void EveryOperatorIsDrawn() =>
        PairGen.Pair.Array[Draws].Sample(
            static pairs => Assert.Equal(Enum.GetValues<MutationOperator>(), pairs.Select(static p => p.Operator).Distinct().Order()),
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: 1,
            print: static pairs => $"{pairs.Length} pairs");

    /// <summary>Ticket P1-017, acceptance criterion 3: each construct is in a preserving pair and in a changing pair, and both sides compile.</summary>
    [Fact]
    public void GeneratesTheIlFallbackConstructs() =>
        DifferentialSoundnessTests.Generated.Array[IlDraws].Sample(
            static pairs =>
            {
                HashSet<(string Construct, bool Preserving)> seen = [];
                foreach ((string legacy, string modern, MutationOperator op) in pairs)
                {
                    seen.UnionWith(Constructs(legacy).Concat(Constructs(modern)).Select(c => (c, PairGen.IsPreserving(op))));
                }

                Assert.Equal(IlConstructs.SelectMany(static c => (IEnumerable<(string, bool)>)[(c, true), (c, false)]).Order(), seen.Order());
            },
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

    /// <summary>The IL fallback constructs <paramref name="source"/>'s method holds, which must compile.</summary>
    private static IEnumerable<string> Constructs(string source)
    {
        Compilation compilation = PairRuntime.Compile(source, "Pair");
        Assert.DoesNotContain(compilation.GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error);
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        MethodDeclarationSyntax method = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        IOperation body = compilation.GetSemanticModel(tree).GetOperation(method, TestContext.Current.CancellationToken)!;
        return body.Descendants().Select(static o => o switch
        {
            IBinaryOperation { IsLifted: true } => IlConstructs[0],
            IConversionOperation conversion when IsNullable(conversion.Type) || IsNullable(conversion.Operand.Type) => IlConstructs[1],
            IInterpolatedStringOperation => IlConstructs[2],
            IRecursivePatternOperation { DeconstructionSubpatterns.Length: > 0, Parent: ISwitchExpressionArmOperation } => IlConstructs[3],
            _ => null,
        }).OfType<string>().Distinct(StringComparer.Ordinal);
    }

    private static bool IsNullable(ITypeSymbol? type) => type?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
}
