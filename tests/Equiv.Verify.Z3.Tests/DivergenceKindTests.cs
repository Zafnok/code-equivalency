using System.Globalization;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis.Sarif;
using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Ticket P1-020 (ADR 0037's clarification of 2026-10-11): a Divergent the solver found carries the two failure queries
/// and a third, whether a result changes, and from the three answers its kind.
/// </summary>
public sealed class DivergenceKindTests
{
    private static readonly VerificationOptions Options = new(3, 10_000, []);

    /// <summary>Returns its argument.</summary>
    private const string Identity = """
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          ret %a
        """;

    /// <summary>Returns its argument plus one.</summary>
    private const string Successor = """
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %one: bv32 = const bv32 1
          %r: bv32 = add %a, %one
          ret %r
        """;

    /// <summary>Returns its argument, and reaches an unshared opaque node when it is 5.</summary>
    private const string OpaqueAtFive = """
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %five: bv32 = const bv32 5
          %is: bool = eq %a, %five
          br %is, B1, B2
        B1:
          %s: sort "string" = opaque "InterpolatedString" at "New.cs" 5:9-5:30
          ret %a
        B2:
          ret %a
        """;

    /// <summary>Counts up to its argument and returns it, throwing when it is 1: one iteration, within the bound.</summary>
    private const string LoopThatThrowsAtOne = """
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %zero: bv32 = const bv32 0
          %one: bv32 = const bv32 1
          goto B1
        B1:
          %i: bv32 = phi [B0: %zero, B2: %next]
          %more: bool = slt %i, %a
          br %more, B2, B3
        B2:
          %next: bv32 = add %i, %one
          goto B1
        B3:
          %is: bool = eq %a, %one
          br %is, B4, B5
        B4:
          throw "System.Exception"
        B5:
          ret %a
        """;

    /// <summary>Criterion 2: a Divergent carries the three answers, over the product its counterexample came from, with the time they took.</summary>
    [Fact]
    public void DivergentCarriesFailureRefinement()
    {
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(IrText.Parse(ThrowsAt(0)), IrText.Parse(Identity), Options));

        FailureRefinement refinement = Assert.IsType<FailureRefinement>(divergent.FailureRefinement);
        Assert.Equal(RefinementResult.NoneProved, refinement.NewFailures);
        Assert.Equal(RefinementOutcome.Found, refinement.RemovedFailures.Outcome);
        Counterexample model = Assert.IsType<Counterexample>(refinement.RemovedFailures.Model);
        Assert.Equal(divergent.Counterexample, model);
        Assert.Equal(RefinementResult.NoneProved, refinement.ResultChanges);
        Assert.Equal(DivergenceKind.RemovedFailuresOnly, refinement.Kind);
        Assert.True(refinement.Elapsed > TimeSpan.Zero);
    }

    /// <summary>
    /// Criterion 3: both sides return and the values differ, so the result changes, with the model. Where the two values
    /// differ only through a shared fragment read with other arguments, the replay is tainted and the answer is Unknown.
    /// </summary>
    [Fact]
    public void ResultChanges_FoundNeedsAnUntaintedReplay()
    {
        FailureRefinement real = Query(Identity, Successor);
        FailureRefinement tainted = Query(
            """
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = opaque "DelegateCreation" at "Old.cs" 3:9-3:30 fragment "f1" reads(%a)
              ret %r
            """,
            """
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %r: bv32 = opaque "DelegateCreation" at "New.cs" 3:9-3:30 fragment "f1" reads(%b)
              ret %r
            """);

        Assert.Equal(RefinementOutcome.Found, real.ResultChanges!.Outcome);
        Counterexample model = Assert.IsType<Counterexample>(real.ResultChanges.Model);
        Assert.IsType<IrReturned>(model.Old.Outcome);
        Assert.IsType<IrReturned>(model.New.Outcome);
        Assert.NotEqual(model.Old.Outcome, model.New.Outcome);
        Assert.Equal(RefinementResult.Unknown, tainted.ResultChanges);
    }

    /// <summary>
    /// Criterion 3: a pair that differs only in whether a side throws, or in which exception it throws, has no input on
    /// which both end the same way and differ, and no opaque node: the third query proves there is none.
    /// </summary>
    [Theory]
    [InlineData("System.ArgumentNullException")]
    [InlineData(null)]
    public void ResultChanges_NoneProvedWhenOnlyTheThrowDiffers(string? modernException)
    {
        FailureRefinement refinement = Query(ThrowsAt(0), modernException is null ? Identity : ThrowsAt(0, modernException));

        Assert.Equal(RefinementResult.NoneProved, refinement.ResultChanges);
    }

    /// <summary>Both throw the same exception type on the same input but return different values elsewhere: a result changes.</summary>
    [Fact]
    public void ResultChanges_FoundWhenBothThrowTheSameTypeAndAnOutDiffers()
    {
        const string Thrower = """
            proc "T::M(int,ref int)" (%a: bv32, ref %o: bv32) entry B0
            B0:
              throw "System.Exception" outs(%o = %o)
            """;

        FailureRefinement refinement = Query(Thrower, Thrower.Replace("%o = %o", "%o = %a", StringComparison.Ordinal));

        Assert.Equal(RefinementOutcome.Found, refinement.ResultChanges!.Outcome);
        Assert.Equal(DivergenceKind.ResultChanged, refinement.Kind);
    }

    /// <summary>Criterion 4, one pair per kind: the backend's Divergent has the kind its three answers give.</summary>
    [Theory]
    [InlineData("removed", DivergenceKind.RemovedFailuresOnly)]
    [InlineData("new", DivergenceKind.NewFailuresOnly)]
    [InlineData("both", DivergenceKind.FailuresOnly)]
    [InlineData("value", DivergenceKind.ResultChanged)]
    [InlineData("type", DivergenceKind.Undetermined)]
    [InlineData("opaque", DivergenceKind.Undetermined)]
    public void DivergenceKind_IsDerivedFromTheThreeAnswers(string pair, DivergenceKind expected)
    {
        (string old, string @new) = pair switch
        {
            "removed" => (ThrowsAt(0), Identity),
            "new" => (Identity, ThrowsAt(0)),
            "both" => (ThrowsAt(0), ThrowsAt(1)),
            "value" => (Identity, Successor),
            "type" => (ThrowsAt(0), ThrowsAt(0, "System.ArgumentNullException")),
            _ => (ThrowsAt(0), OpaqueAtFive),
        };

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options));

        Assert.Equal(expected, divergent.FailureRefinement!.Kind);
    }

    /// <summary>
    /// Criterion 4's table on the answers alone, every combination: a found result change decides the kind, a found failure
    /// needs the other two answers proved, and anything else is undetermined. An Unknown's two answers have no kind.
    /// </summary>
    [Fact]
    public void DivergenceKind_ReadsNothingButTheThreeAnswers()
    {
        RefinementResult found = new(RefinementOutcome.Found);
        RefinementResult[] answers = [RefinementResult.NoneProved, found, RefinementResult.Unknown];

        foreach (RefinementResult newFailures in answers)
        {
            foreach (RefinementResult removedFailures in answers)
            {
                foreach (RefinementResult resultChanges in answers)
                {
                    DivergenceKind expected = (newFailures.Outcome, removedFailures.Outcome, resultChanges.Outcome) switch
                    {
                        (_, _, RefinementOutcome.Found) => DivergenceKind.ResultChanged,
                        (_, _, RefinementOutcome.Unknown) or (RefinementOutcome.Unknown, _, _) or (_, RefinementOutcome.Unknown, _) => DivergenceKind.Undetermined,
                        (RefinementOutcome.Found, RefinementOutcome.Found, _) => DivergenceKind.FailuresOnly,
                        (RefinementOutcome.Found, _, _) => DivergenceKind.NewFailuresOnly,
                        (_, RefinementOutcome.Found, _) => DivergenceKind.RemovedFailuresOnly,
                        _ => DivergenceKind.Undetermined,
                    };

                    Assert.Equal(expected, new FailureRefinement(newFailures, removedFailures) { ResultChanges = resultChanges }.Kind);
                }
            }
        }

        Assert.Null(new FailureRefinement(found, RefinementResult.NoneProved).Kind);
    }

    /// <summary>
    /// Criterion 5 on one pair: with the refinement dropped, the Divergent is the verdict the backend gave, and its SARIF
    /// result has the same rule id, level, kind, fingerprints and review group. Only the two properties and the message's
    /// last sentence go.
    /// </summary>
    [Fact]
    public void DivergenceKind_ChangesNoFingerprintOrRule()
    {
        IrProcedure old = IrText.Parse(ThrowsAt(0));
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(old, IrText.Parse(Identity), Options));

        Result with = Sarif(old, divergent);
        Result without = Sarif(old, divergent with { FailureRefinement = null });

        Assert.Equal("removed-failures-only", with.GetProperty<string>("divergenceKind"));
        Assert.EndsWith(" The modern side differs only where the legacy side throws.", with.Message.Text, StringComparison.Ordinal);
        Assert.Equal(without.Message.Text + " The modern side differs only where the legacy side throws.", with.Message.Text);
        Assert.False(without.TryGetProperty("divergenceKind", out string? _));
        Assert.False(without.TryGetProperty("failureRefinement", out Dictionary<string, object>? _));
        Assert.Equal("EQ002", with.RuleId);
        Assert.Equal((without.RuleId, without.Level, without.Kind), (with.RuleId, with.Level, with.Kind));
        Assert.Equal(without.PartialFingerprints.OrderBy(static p => p.Key, StringComparer.Ordinal), with.PartialFingerprints.OrderBy(static p => p.Key, StringComparer.Ordinal));
        Assert.Equal(without.GetProperty<string>("reviewGroup"), with.GetProperty<string>("reviewGroup"));
    }

    /// <summary>
    /// Criterion 6: a looping pair is asked on rung 1's unrolled product. The modern side throws after one iteration, which
    /// is found; past the bound it may end any way, so no result change is not proved there, and the kind is undetermined.
    /// </summary>
    [Fact]
    public void LoopingPair_PastTheBoundIsUnknown()
    {
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(IrText.Parse(Identity), IrText.Parse(LoopThatThrowsAtOne), Options));

        FailureRefinement refinement = divergent.FailureRefinement!;
        Assert.Equal(RefinementOutcome.Found, refinement.NewFailures.Outcome);
        Assert.Equal(RefinementResult.NoneProved, refinement.RemovedFailures);
        Assert.Equal(RefinementResult.Unknown, refinement.ResultChanges);
        Assert.Equal(DivergenceKind.Undetermined, refinement.Kind);
    }

    /// <summary>A looping pair no input takes past the bound is decided like an acyclic one: the modern loop runs once on every input.</summary>
    [Fact]
    public void LoopingPair_WithinTheBoundIsDecided()
    {
        string bounded = LoopThatThrowsAtOne.Replace("slt %i, %a", "slt %i, %one", StringComparison.Ordinal);

        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(IrText.Parse(Identity), IrText.Parse(bounded), Options));

        Assert.Equal(DivergenceKind.NewFailuresOnly, divergent.FailureRefinement!.Kind);
    }

    /// <summary>A third query the solver gives up on is Unknown: deciding whether the two returned values differ needs the hard arithmetic.</summary>
    [Fact]
    public void ResultChanges_SolverGivesUp_IsUnknown()
    {
        Fixture fixture = Fixture.Load("hard-multiplication");
        VerificationOptions tiny = Options with { TimeoutMs = 600_000, ResourceLimit = 1_000 };

        FailureRefinement refinement = new FailureRefinementQuery(static () => new Context(), tiny).Run(fixture.Old, fixture.New, encodable: true, resultChanges: true);

        Assert.Equal(new FailureRefinement(RefinementResult.NoneProved, RefinementResult.NoneProved) { ResultChanges = RefinementResult.Unknown }, refinement);
    }

    /// <summary>A pair with no product has three Unknown answers, whether rung 1 said so or the unrolling refuses it.</summary>
    [Fact]
    public void PairWithNoProduct_HasThreeUnknownAnswers()
    {
        IrProcedure small = IrText.Parse(Identity);
        IrProcedure deep = IrText.Parse(DeepLoops.Nested(depth: 12));
        FailureRefinementQuery query = new(static () => new Context(), Options);
        FailureRefinement unanswered = new(RefinementResult.Unknown, RefinementResult.Unknown) { ResultChanges = RefinementResult.Unknown };

        Assert.Equal(unanswered, query.Run(small, small, encodable: false, resultChanges: true));
        Assert.Equal(unanswered, query.Run(deep, deep, encodable: true, resultChanges: true));
        Assert.Equal(DivergenceKind.Undetermined, unanswered.Kind);
    }

    /// <summary>
    /// The claim behind <c>removed-failures-only</c>: for a generated procedure against itself behind a guard that throws
    /// when its first integer parameter is 0, reported with that kind, every generated input on which the legacy side
    /// returns gives equal observables in <see cref="IrInterpreter"/>.
    /// </summary>
    [Fact]
    public void RemovedFailuresOnly_AgreesWhereverTheLegacySideReturns()
    {
        int reported = 0;
        IrGen.AcyclicProcedure
            .Where(static p => p.Parameters.Any(static a => a is { Var.Type: IrBitVec { Width: 32 }, Kind: IrParameterKind.In }))
            .SelectMany(static p => IrGen.Inputs(p).Array[32].Select(inputs => (Modern: p, Inputs: inputs)))
            .Sample(
                sample =>
                {
                    IrProcedure legacy = Guarded(sample.Modern);
                    if (new Z3Backend().Verify(legacy, sample.Modern, Options) is not Divergent { FailureRefinement.Kind: DivergenceKind.RemovedFailuresOnly })
                    {
                        return;
                    }

                    reported++;
                    (IrProcedure old, IrProcedure @new, _) = ProductEncoder.ShareFragments(legacy, sample.Modern);
                    foreach (IrInputs inputs in sample.Inputs)
                    {
                        IrRun oldRun = IrGen.Run(old, inputs);
                        if (oldRun.Outcome is IrReturned)
                        {
                            Assert.Equal(oldRun, IrGen.Run(@new, inputs));
                        }
                    }
                },
                iter: 100,
                print: static sample => IrText.Dump(sample.Modern));

        Assert.True(reported > 0, "no generated pair was removed-failures-only");
    }

    /// <summary><paramref name="procedure"/> behind a new entry block that throws when its first <c>int</c> parameter is 0.</summary>
    private static IrProcedure Guarded(IrProcedure procedure)
    {
        IrVar parameter = procedure.Parameters.First(static a => a is { Var.Type: IrBitVec { Width: 32 }, Kind: IrParameterKind.In }).Var;
        string text = IrText.Dump(procedure);
        string[] byRef = [.. procedure.Parameters.Where(static a => a.Kind != IrParameterKind.In).Select(static a => $"%{a.Var.Name} = %{a.Var.Name}")];
        string outs = byRef.Length == 0 ? string.Empty : $" outs({string.Join(", ", byRef)})";
        int guard = procedure.Blocks.Max(static b => b.Id.Value) + 1;
        string entry = string.Create(CultureInfo.InvariantCulture, $" entry B{procedure.Entry.Value}\n");
        int header = text.IndexOf(entry, StringComparison.Ordinal);
        return IrText.Parse(string.Create(
            CultureInfo.InvariantCulture,
            $"{text[..header]} entry B{guard}\n{text[(header + entry.Length)..].TrimEnd()}\nB{guard}:\n  %guardzero: bv32 = const bv32 0\n  %guardis: bool = eq %{parameter.Name}, %guardzero\n  br %guardis, B{guard + 1}, B{procedure.Entry.Value}\nB{guard + 1}:\n  throw \"System.ArgumentNullException\"{outs}\n"));
    }

    /// <summary>Returns its argument, and throws <paramref name="exception"/> when it is <paramref name="at"/>.</summary>
    private static string ThrowsAt(int at, string exception = "System.Exception") => $"""
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %at: bv32 = const bv32 {at.ToString(CultureInfo.InvariantCulture)}
          %is: bool = eq %a, %at
          br %is, B1, B2
        B1:
          throw "{exception}"
        B2:
          ret %a
        """;

    private static Result Sarif(IrProcedure procedure, Verdict verdict) =>
        SarifReportWriter.Write([new VerificationResult(procedure.Identity, verdict)]).Runs[0].Results[0];

    private static FailureRefinement Query(string old, string @new) =>
        new FailureRefinementQuery(static () => new Context(), Options).Run(IrText.Parse(old), IrText.Parse(@new), encodable: true, resultChanges: true);
}
