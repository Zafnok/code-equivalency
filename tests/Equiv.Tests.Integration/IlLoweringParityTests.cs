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
/// representational difference that keeps it from being proved, and nothing else may join them.
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
    }.ToImmutableDictionary(StringComparer.Ordinal);

    private const string ConfirmAsync = "Equiv.Samples.BusinessLayer.OrderService::ConfirmAsync(System.Threading.Tasks.Task<global::Equiv.Samples.BusinessLayer.Order>)";

    private const string Export = "Equiv.Samples.BusinessLayer.OrderService::Export(Equiv.Samples.BusinessLayer.Order)";

    private const string Reserve = "Equiv.Samples.BusinessLayer.OrderService::Reserve(Equiv.Samples.BusinessLayer.Order,int)";

    /// <summary>An <c>async</c> method's IL is its state machine's kickoff, whose ILAst is opaque (out of scope; P1-012).</summary>
    private const string StateMachine = "the IL of an async method is its state machine's kickoff, which is opaque";

    /// <summary>
    /// The IOperation lowering null-checks a <c>using</c> resource's call of <c>Dispose</c> on its conversion to
    /// <c>IDisposable</c>, whose nullness it reads from <c>null.System.IDisposable</c>, not tied to the resource's (M3-010);
    /// the IL lowering reads the resource's own null shadow, which the <c>finally</c> has just tested.
    /// </summary>
    private const string UsingResource = "IOperation null-checks the using resource's IDisposable conversion through null.System.IDisposable";

    /// <summary>
    /// The IOperation lowering reads <c>order != null</c> as the nullness of <c>order</c> converted to <c>object</c>, from
    /// <c>null.System.Object</c> (M3-010); the IL, which compares the reference itself, reads <c>order</c>'s null shadow.
    /// </summary>
    private const string NullTestOfAConversion = "IOperation reads order != null through null.System.Object of the conversion to object";

    private static readonly VerificationOptions Options = new(EquivConfig.Default.Bound, EquivConfig.Default.TimeoutMs, []);

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task IlAndOperationLoweringsOfOpaqueFreeSamplesAreEquivalent()
    {
        Dictionary<string, string> different = new(StringComparer.Ordinal);
        int proved = 0;
        foreach (string solution in Directory.GetDirectories(Path.Combine(RepoRoot, "samples")).Order(StringComparer.Ordinal).SelectMany(Solutions))
        {
            LoadedSolution loaded = await new MsBuildSolutionLoader().LoadAsync(solution, TestContext.Current.CancellationToken);
            foreach (Compilation compilation in loaded.Compilations)
            {
                foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
                {
                    IrProcedure operation = IrLowerer.Lower(procedure.Symbol, compilation, RenameMap.Empty, [], Runtimes.Migration);
                    if (operation.Blocks.SelectMany(static b => b.Instructions).Any(static i => i is IrOpaque))
                    {
                        continue;
                    }

                    Verdict verdict = new Z3Backend().Verify(operation, IlLowerer.Lower(procedure.Symbol, compilation, Runtimes.Migration), Options);
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
