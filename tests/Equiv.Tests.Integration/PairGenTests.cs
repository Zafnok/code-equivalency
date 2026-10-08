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

    /// <summary>Pairs drawn to find every shape of closure.</summary>
    private const int ClosureDraws = 60;

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

    /// <summary>
    /// Ticket P2-079, acceptance criterion 5: a closure pair's two sides compile and differ in one line, which is the
    /// lambda or the local function, and a lambda, a call of a local function and a local function's method group are all drawn.
    /// </summary>
    [Fact]
    public void ClosurePairsDifferOnlyInsideALambdaOrALocalFunction() =>
        PairGen.ClosurePair.Array[ClosureDraws].Sample(
            static pairs =>
            {
                HashSet<string> shapes = new(StringComparer.Ordinal);
                foreach ((string legacy, string modern, MutationOperator op) in pairs)
                {
                    Assert.Equal(MutationOperator.ChangeConstant, op);
                    Assert.All((string[])[legacy, modern], static source => Assert.DoesNotContain(PairRuntime.Compile(source, "Pair").GetDiagnostics(TestContext.Current.CancellationToken), static d => d.Severity == DiagnosticSeverity.Error));
                    string[] before = legacy.Split('\n');
                    string[] after = modern.Split('\n');
                    Assert.Equal(before.Length, after.Length);
                    string[] changed = [.. before.Zip(after).Where(static l => !string.Equals(l.First, l.Second, StringComparison.Ordinal)).Select(static l => l.First)];

                    // A closure after a return is not rendered, and then the two sides are one text.
                    Assert.True(changed.Length <= 1, legacy + modern);
                    shapes.UnionWith(changed.Select(line => Shape(line, legacy)));
                }

                Assert.Equal(["call of a local function", "lambda", "method group of a local function"], shapes.Order(StringComparer.Ordinal), StringComparer.Ordinal);
            },
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: 1,
            print: static pairs => $"{pairs.Length} pairs");

    /// <summary>The closure the changed <paramref name="line"/> of <paramref name="source"/> is, which must be one.</summary>
    private static string Shape(string line, string source)
    {
        if (line.Contains("(v => ", StringComparison.Ordinal))
        {
            return "lambda";
        }

        Assert.Contains("int L(int v) => ", line, StringComparison.Ordinal);
        return source.Contains("(System.Func<int, int>)L)", StringComparison.Ordinal) ? "method group of a local function" : "call of a local function";
    }

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

    /// <summary>
    /// Ticket P1-030 criterion 5: the floating-point pairs compile, take <c>float g</c> and <c>double h</c>, use the
    /// interpreted operators and comparisons, and come from both families.
    /// </summary>
    [Fact]
    public void FloatPairsCompileAndUseTheInterpretedOperatorsInBothFamilies()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        int preserving = 0;
        int changing = 0;
        PairGen.FloatPair.Sample(
            pair =>
            {
                Assert.NotEqual(pair.LegacySource, pair.ModernSource, StringComparer.Ordinal);
                foreach (string source in (string[])[pair.LegacySource, pair.ModernSource])
                {
                    Assert.Contains("string s, int[] u, float g, double h)", source, StringComparison.Ordinal);
                    Assert.Empty(PairRuntime.Compile(source, "Float").GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
                    lock (seen)
                    {
                        seen.UnionWith(((string[])[" + ", " - ", " * ", " / ", "(-", " < ", " <= ", " > ", " >= ", " == ", " != ", "(double)", "(float)"]).Where(t => source.Contains(t, StringComparison.Ordinal)));
                    }
                }

                Interlocked.Increment(ref PairGen.IsPreserving(pair.Operator) ? ref preserving : ref changing);
            },
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: 300);

        Assert.Equal(13, seen.Count);
        Assert.True(preserving >= 60 && changing >= 60, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{preserving} preserving and {changing} changing of 300"));
    }

    /// <summary>Ticket P1-030 criterion 5: NaN, both zeros, both infinities and a subnormal are among the values an input gives <c>g</c> and <c>h</c>.</summary>
    [Fact]
    public void FloatInputsHoldEveryEdgeValue()
    {
        List<(float G, double H)> drawn = [];
        PairGen.FloatInput.Sample(
            input =>
            {
                lock (drawn)
                {
                    drawn.Add(input.Floats!.Value);
                }

                Assert.Matches(@" g=\S+ h=\S+$", input.ToString());
            },
            seed: DifferentialSoundnessTests.Budget.Seed,
            iter: 2000,
            threads: 1);

        Assert.Contains(drawn, static v => double.IsNaN(v.H));
        Assert.Contains(drawn, static v => float.IsNaN(v.G));
        Assert.Contains(drawn, static v => v.H == 0 && !double.IsNegative(v.H));
        Assert.Contains(drawn, static v => v.H == 0 && double.IsNegative(v.H));
        Assert.Contains(drawn, static v => v.G == 0 && float.IsNegative(v.G));
        Assert.Contains(drawn, static v => double.IsPositiveInfinity(v.H));
        Assert.Contains(drawn, static v => double.IsNegativeInfinity(v.H));
        Assert.Contains(drawn, static v => float.IsInfinity(v.G));
        Assert.Contains(drawn, static v => double.IsSubnormal(v.H));
        Assert.Contains(drawn, static v => float.IsSubnormal(v.G));
        Assert.Contains(drawn, static v => v.H == double.MaxValue);
        Assert.Null(new PairInput(1, 2, 3, 4, E: true, SIsNull: false, 5, U: null).Floats);
        Assert.EndsWith("u=null", new PairInput(1, 2, 3, 4, E: true, SIsNull: false, 5, U: null).ToString(), StringComparison.Ordinal);
    }
}
