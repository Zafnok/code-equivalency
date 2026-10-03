using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp;
using Equiv.Frontend.CSharp.Loading;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.Verify.Z3;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Tests.Integration;

/// <summary>
/// Ticket P1-015 acceptance criterion 3 (ADR 0039): each <c>samples/</c> method, loaded as a run loads it, whose IOperation
/// lowering holds no <see cref="IrOpaque"/>, lowered again from its IL, is Equivalent to its IOperation lowering when
/// <see cref="Z3Backend"/> verifies the two as a pair. The methods that are not are <see cref="Known"/>, each with the
/// representational difference that keeps it from being proved, and nothing else may join them. Both lowerings are of one
/// body on one runtime, so they are lowered as a same-runtime pair is (ADR 0040; ticket P2-055): no runtime rule applies.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IlLoweringParityTests
{
    /// <summary>The methods whose two lowerings differ in representation only, by identity, with why.</summary>
    private static readonly ImmutableDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [$"business-layer/legacy {ConfirmAsync}"] = StateMachine,
        [$"business-layer/modern {ConfirmAsync}"] = StateMachine,
        [$"business-layer/legacy {Export}"] = UsingResource,
        [$"business-layer/modern {Export}"] = UsingResource,
        [$"business-layer/legacy {Reserve}"] = NullTestOfAConversion,
        [$"business-layer/modern {Reserve}"] = NullTestOfAConversion,
        [$"business-layer/legacy {SkusOver}"] = Lambda,
        [$"business-layer/modern {SkusOver}"] = Lambda,
        [$"business-layer/legacy {CappedLineCount}"] = Lambda,
        [$"business-layer/modern {CappedLineCount}"] = Lambda,
        [$"cleanup-modern-syntax/legacy {Positives}"] = NullTestOfAConversion,
        [$"cleanup-modern-syntax/modern {Positives}"] = Lambda,
        [$"cleanup-modern-syntax/modern {Join}"] = InterpolatedString,
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private const string ConfirmAsync = "Equiv.Samples.BusinessLayer.OrderService::ConfirmAsync(System.Threading.Tasks.Task<global::Equiv.Samples.BusinessLayer.Order>)";

    private const string Export = "Equiv.Samples.BusinessLayer.OrderService::Export(Equiv.Samples.BusinessLayer.Order)";

    private const string Reserve = "Equiv.Samples.BusinessLayer.OrderService::Reserve(Equiv.Samples.BusinessLayer.Order,int)";

    private const string SkusOver = "Equiv.Samples.BusinessLayer.OrderService::SkusOver(Equiv.Samples.BusinessLayer.Order,int)";

    private const string CappedLineCount = "Equiv.Samples.BusinessLayer.OrderService::CappedLineCount(Equiv.Samples.BusinessLayer.Order,int)";

    private const string Positives = "Equiv.Samples.CleanupModernSyntax.Tidy::Positives(System.Collections.Generic.List<int>)";

    private const string Join = "Equiv.Samples.CleanupModernSyntax.Tidy::Join(string,string)";

    /// <summary>An <c>async</c> method's IL is its state machine's kickoff, whose ILAst is opaque (out of scope; P1-012).</summary>
    private const string StateMachine = "the IL of an async method is its state machine's kickoff, which is opaque";

    /// <summary>
    /// The IOperation lowering null-checks a <c>using</c> resource's call of <c>Dispose</c> on its conversion to
    /// <c>IDisposable</c>, whose nullness it reads from <c>null.System.IDisposable</c>, not tied to the resource's (M3-010);
    /// the IL lowering reads the resource's own null shadow, which the <c>finally</c> has just tested.
    /// </summary>
    private const string UsingResource = "IOperation null-checks the using resource's IDisposable conversion through null.System.IDisposable";

    /// <summary>
    /// The IOperation lowering reads <c>order != null</c> (and <c>values == null</c>) as the nullness of the reference
    /// converted to <c>object</c>, from <c>null.System.Object</c> (M3-010); the IL, which compares the reference itself,
    /// reads the reference's own null shadow.
    /// </summary>
    private const string NullTestOfAConversion = "IOperation reads a null test of a reference through null.System.Object of the conversion to object";

    /// <summary>
    /// The IOperation lowering makes a lambda the pure function <c>delegate:&lt;fingerprint&gt;</c> of its bound body
    /// (ticket P2-067), so the method holds no opaque; the IL has no bound body to fingerprint, and leaves the lambda's
    /// <c>LdFtn[lambda]</c> and its closure class opaque (ADR 0039).
    /// </summary>
    private const string Lambda = "IOperation lowers a lambda as delegate:<fingerprint>; the IL leaves LdFtn[lambda] opaque";

    /// <summary>
    /// The IOperation lowering joins an interpolated string's parts with the two-argument <c>String.Concat</c>, one call
    /// for each part after the first (ticket P2-086); the compiler emits one call of the three-argument overload, which
    /// the IL lowering names as it is.
    /// </summary>
    private const string InterpolatedString = "IOperation lowers an interpolated string as a chain of Concat(string,string); the IL calls Concat(string,string,string)";

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent()
    {
        Dictionary<string, string> different = new(StringComparer.Ordinal);
        int proved = 0;
        foreach (string solution in Directory.GetDirectories(Path.Combine(RepoRoot, "samples")).Order(StringComparer.Ordinal).SelectMany(Solutions))
        {
            Codebase side = solution.EndsWith(".sln", StringComparison.Ordinal) ? Codebase.Legacy : Codebase.Modern;
            LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(solution, side, TestContext.Current.CancellationToken);

            // A project that does not compile emits no IL to read (samples/partly-compiling-modern; ticket P2-085).
            foreach (Compilation compilation in loaded.Compilations.Where(static c => !c.GetDiagnostics(TestContext.Current.CancellationToken).Any(static d => d.Severity == DiagnosticSeverity.Error)))
            {
                foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
                {
                    IrProcedure operation = IrLowerer.Lower(procedure.Symbol, compilation, RenameMap.Empty, [], Runtimes.SameRuntime);
                    if (operation.Blocks.SelectMany(static b => b.Instructions).Any(static i => i is IrOpaque))
                    {
                        continue;
                    }

                    Verdict verdict = new Z3Backend().Verify(operation, IlLowerer.Lower(procedure.Symbol, compilation, Runtimes.SameRuntime), Options);
                    if (verdict is Equivalent)
                    {
                        proved++;
                    }
                    else
                    {
                        different[$"{Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(solution)))}/{Path.GetFileName(Path.GetDirectoryName(solution))} {procedure.Identity.Value}"] = verdict.ToString()!;
                    }
                }
            }
        }

        Assert.True(
            different.Keys.Order(StringComparer.Ordinal).SequenceEqual(Known.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            string.Join('\n', different.Select(static d => $"{d.Key}: {d.Value}")));
        Assert.True(proved > 40, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"only {proved} sample methods proved"));
    }

    private static IEnumerable<string> Solutions(string sample) =>
        [
            .. Directory.GetFiles(Path.Combine(sample, "legacy"), "*.sln"),
            .. Directory.GetFiles(Path.Combine(sample, "modern"), "*.slnx"),
        ];
}
