using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.TestSupport;

using Equiv.Verify.Z3.Contracts;
using Equiv.Verify.Z3.Refinement;

using Microsoft.Z3;

using Xunit;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Caller-sufficient callee contracts (ADR 0036 decision 2; ticket P1-010): <see cref="ContractVerifier"/> admits or
/// rejects a candidate K for a callee pair, <see cref="ContractSearch"/> builds K from what the caller observes and drops
/// what a model falsifies, and <see cref="Z3Backend.VerifyUnderContracts"/> re-verifies the caller with each side's call
/// fresh and related only by K.
/// </summary>
public sealed class ContractTests
{
    /// <summary><c>Score</c> changes only for inputs whose score was already above 10: it is one lower there.</summary>
    internal const string Score = """
        proc "T::Score(int)" (%a: bv32) -> bv32 entry B0
        B0:
          ret %a
        ---
        proc "T::Score(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %ten: bv32 = const bv32 10
          %big: bool = sgt %a, %ten
          br %big, B1, B2
        B1:
          %one: bv32 = const bv32 1
          %b: bv32 = sub %a, %one
          ret %b
        B2:
          ret %a
        """;

    /// <summary><c>Classify(a) => Score(a) > 0 ? 1 : 0</c>, the same on both sides.</summary>
    private const string Classify = """
        proc "T::Classify(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          br %t, B3, B1
        B1:
          %zero: bv32 = const bv32 0
          %p: bool = sgt %r, %zero
          br %p, B2, B4
        B2:
          %yes: bv32 = const bv32 1
          ret %yes
        B4:
          ret %zero
        B3:
          throw "System.Exception"
        """;

    /// <summary>As <see cref="Classify"/>, but a positive score is returned, so the caller sees the change.</summary>
    private const string Leak = """
        proc "T::Leak(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          br %t, B3, B1
        B1:
          %zero: bv32 = const bv32 0
          %p: bool = sgt %r, %zero
          br %p, B2, B4
        B2:
          ret %r
        B4:
          ret %zero
        B3:
          throw "System.Exception"
        """;

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    private static ContractConjunct Threw => new(ConjunctKind.Threw);

    [Fact]
    public void ContractVerifier_AdmitsWhenProductSatisfiesK()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        CalleeContract contract = Candidate(Classify, Score);

        ContractCheck check = Verifier().Verify(old, @new, contract);

        Assert.Equal(new ContractCheck.Admitted(ProofMethod.Bounded), check);
        Assert.Contains(contract.Conjuncts, static c => c.Kind == ConjunctKind.Predicate);
    }

    [Fact]
    public void ContractVerifier_RejectsWithModel()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        CalleeContract contract = Candidate(Equals11, Score);
        int predicate = contract.Conjuncts.IndexOf(contract.Conjuncts.Single(static c => c.Kind == ConjunctKind.Predicate));

        ContractModel model = Assert.IsType<ContractCheck.Rejected>(Verifier().Verify(old, @new, contract)).Model;

        Assert.Equal([predicate], model.Falsified);
        Assert.Contains("in.a", model.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caller checks <c>Score(a) == 11</c>, <c>Score(a) &gt; 0</c> and their conjunction: every input that breaks the
    /// first also breaks the third, so both are dropped, and the second is admitted.
    /// </summary>
    [Fact]
    public void ObservedPredicates_DropFalsifiedConjuncts()
    {
        (IrProcedure caller, _) = Fixture.Pair(Both + "\n---\n" + Both);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        ContractSearch search = new(Context, Options);
        CalleeContract candidate = search.Candidate(caller, caller, new CalleePair("T::Score(int)", old, @new));

        CalleeContract admitted = search.Admit(caller, caller, new CalleePair("T::Score(int)", old, @new))!;

        Assert.Equal(3, candidate.Conjuncts.Count(static c => c.Kind == ConjunctKind.Predicate));
        Assert.Equal(candidate.Conjuncts.Length - 2, admitted.Conjuncts.Length);
        using Context context = new();
        string text = admitted.Text(context);
        Assert.Contains("bvsgt r.old #x00000000", text, StringComparison.Ordinal);
        Assert.DoesNotContain("#x0000000b", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASearchThatRunsOutOfRoundsAdmitsNothing()
    {
        (IrProcedure caller, _) = Fixture.Pair(Both + "\n---\n" + Both);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);

        Assert.Null(new ContractSearch(Context, Options) { MaxRounds = 1 }.Admit(caller, caller, new CalleePair("T::Score(int)", old, @new)));
    }

    /// <summary>Criterion 1's Unknown: a solver that gives up decides nothing.</summary>
    [Fact]
    public void ContractVerifier_TimeoutIsUnknown()
    {
        Fixture hard = Fixture.Load("hard-multiplication");
        CalleeContract contract = new([new ContractConjunct(ConjunctKind.Predicate, Predicate: Positive(new IrBitVec(64)))]);

        ContractCheck check = new ContractVerifier(Context, Options with { TimeoutMs = 50 }).Verify(hard.Old, hard.New, contract);

        Assert.Contains("solver returned unknown", Assert.IsType<ContractCheck.Unknown>(check).Reason, StringComparison.Ordinal);
    }

    /// <summary>Ticket P1-032: a callee pair too large to unroll has no product to check a contract on, so the check is Unknown.</summary>
    [Fact]
    public void ContractVerifier_APairTooLargeToUnrollIsUnknown()
    {
        IrProcedure deep = IrText.Parse(DeepLoops.Nested(depth: 12));

        ContractCheck check = Verifier().Verify(deep, deep, new CalleeContract([Threw]));

        Assert.Equal(new ContractCheck.Unknown("a side unrolled 3 times holds more than 25000 blocks"), check);
    }

    [Theory]
    [InlineData("loops/recursion-aligned")]
    [InlineData("loops/irreducible")]
    public void ContractVerifier_ASelfCallOrIrreducibleFlowIsUnknown(string fixture)
    {
        Fixture pair = Fixture.Load(fixture);

        ContractCheck check = Verifier().Verify(pair.Old, pair.New, new CalleeContract([Threw]));

        Assert.Equal(new ContractCheck.Unknown("a side calls itself or has irreducible control flow"), check);
    }

    [Theory]
    [InlineData("loops/aligned-unchanged", ProofMethod.LockstepInduction)]
    [InlineData("loops/warm-up", ProofMethod.KInduction)]
    public void ContractVerifier_ALoopingPairIsAdmittedByInduction(string fixture, ProofMethod by)
    {
        Fixture pair = Fixture.Load(fixture);
        CalleeContract contract = new([Threw, new ContractConjunct(ConjunctKind.Predicate, Predicate: Positive(new IrBitVec(32)))]);

        Assert.Equal(new ContractCheck.Admitted(by), Verifier().Verify(pair.Old, pair.New, contract));
    }

    [Fact]
    public void ContractVerifier_ALoopingPairNoInductionProvesIsUnknown()
    {
        Fixture pair = Fixture.Load("loops/trip-count-changed");
        CalleeContract contract = new([Threw]);

        ContractCheck check = Verifier().Verify(pair.Old, pair.New, contract);

        Assert.StartsWith("no rung decided the contract past the bound 3", Assert.IsType<ContractCheck.Unknown>(check).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyUnderContracts_AnInvisibleChangeIsEquivalentWithItsContract()
    {
        (IrProcedure caller, _) = Fixture.Pair(Classify + "\n---\n" + Classify);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);

        Equivalent proved = new Z3Backend().VerifyUnderContracts(caller, caller, [new CalleePair("T::Score(int)", old, @new)], Options)!;

        Assert.Equal(ProofMethod.Bounded, proved.Method);
        ContractUse contract = Assert.Single(proved.ContractsUsed);
        Assert.Equal("T::Score(int)", contract.Callee);
        Assert.Equal(ObservedPredicates.Name, contract.ProposedBy);
        Assert.Equal(
            "(and (= threw.old threw.new) (=> (and threw.old threw.new) (= type.old type.new)) (= calls.old calls.new) "
            + "(or threw.old threw.new (= (bvsgt r.old #x00000000) (bvsgt r.new #x00000000))))",
            contract.Contract);

        // Ticket P1-031: the caller's ladder under contracts asks rung 1 as the backend is set to, and the abstracted
        // product relates the calls by the same contracts.
        Equivalent abstracted = new Z3Backend { Arithmetic = ArithmeticMode.Forced }.VerifyUnderContracts(caller, caller, [new CalleePair("T::Score(int)", old, @new)], Options)!;
        Assert.Equal(proved.ContractsUsed, abstracted.ContractsUsed);
        Assert.Null(Assert.Single(proved.Ladder).FactsAdded);
        Assert.Equal(proved.Ladder[0] with { Detail = "arithmetic abstracted, round 1: " + proved.Ladder[0].Detail, FactsAdded = 0 }, Assert.Single(abstracted.Ladder));
    }

    /// <summary>
    /// Ticket P2-076 criterion 4: the contracts pass logs its time by stage as the ladder does. The search for the callee's
    /// contract is one step, holding the stages of each candidate it checks, and the caller's ladder follows it.
    /// </summary>
    [Fact]
    public void VerifyUnderContracts_LogsTheContractSearchAsAStepBeforeTheLadder()
    {
        (IrProcedure caller, _) = Fixture.Pair(Classify + "\n---\n" + Classify);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        RecordingRunLog log = new(isDebug: true);

        new Z3Backend().VerifyUnderContracts(caller, caller, [new CalleePair("T::Score(int)", old, @new)], Options with { Log = log });

        Assert.Equal(
            "unroll encode assert inline check:contract=unsat dispose step:contract-search "
            + "share shape unroll encode assert inline check:divergence=unsat assert inline check:opaque=unsat dispose rung:bounded=unsat",
            string.Join(' ', BackendProgressTests.Details(log)));
    }

    [Fact]
    public void VerifyUnderContracts_ACallerThatSeesTheChangeIsNotEquivalent()
    {
        (IrProcedure caller, _) = Fixture.Pair(Leak + "\n---\n" + Leak);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);

        Assert.Null(new Z3Backend().VerifyUnderContracts(caller, caller, [new CalleePair("T::Score(int)", old, @new)], Options));
    }

    /// <summary>
    /// ADR 0036's pitfall at the IR level: K plus the shared function proves <see cref="Leak"/> Equivalent, although it
    /// returns 11 on the legacy side and 12 on the modern side at a = 11. The backend's own encoding keeps each side's
    /// call fresh.
    /// </summary>
    [Fact]
    public void SharedFunctionUnderContractWouldBeUnsound()
    {
        (IrProcedure caller, _) = Fixture.Pair(Leak + "\n---\n" + Leak);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        ImmutableArray<CalleePair> callees = [new CalleePair("T::Score(int)", old, @new)];

        Equivalent? naive = new Z3Backend(Context) { ContractEncoding = new SharedFunctionEncoding() }.VerifyUnderContracts(caller, caller, callees, Options);

        Assert.NotNull(naive);
        Assert.Same(FreshPerSideEncoding.Instance, new Z3Backend().ContractEncoding);
        Assert.True(FreshPerSideEncoding.Instance.FreshPerSide);
    }

    [Theory]
    [InlineData("opaque")]
    [InlineData("by-ref")]
    [InlineData("return-type")]
    public void AnIneligibleCalleeGetsNoContract(string why)
    {
        (IrProcedure caller, _) = Fixture.Pair(Classify + "\n---\n" + Classify);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Score);
        IrProcedure changed = why switch
        {
            "opaque" => IrText.Parse("""
                proc "T::Score(int)" (%a: bv32) -> bv32 entry B0
                B0:
                  %o: bv32 = opaque "unsupported" at "S.cs" 1:1-1:2
                  ret %o
                """),
            "by-ref" => IrText.Parse("""
                proc "T::Score(int)" (ref %a: bv32) -> bv32 entry B0
                B0:
                  ret %a outs(%a = %a)
                """),
            _ => IrText.Parse("""
                proc "T::Score(int)" (%a: bv32) -> bv64 entry B0
                B0:
                  %w: bv64 = sext %a
                  ret %w
                """),
        };

        Assert.Null(new ContractSearch(Context, Options).Admit(caller, caller, new CalleePair("T::Score(int)", old, changed)));
        Assert.Null(new Z3Backend().VerifyUnderContracts(caller, caller, [new CalleePair("T::Score(int)", changed, @new)], Options));
    }

    /// <summary>The two sides throw different exception types, which the caller cannot see; no weaker contract helps.</summary>
    [Fact]
    public void AFalsifiedConjunctTheCallerCannotSeeEndsTheSearch()
    {
        (IrProcedure caller, _) = Fixture.Pair(Classify + "\n---\n" + Classify);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Score(int)" (%a: bv32) -> bv32 entry B0
            B0:
              throw "System.ArgumentException"
            ---
            proc "T::Score(int)" (%a: bv32) -> bv32 entry B0
            B0:
              throw "System.InvalidOperationException"
            """);

        Assert.Null(new ContractSearch(Context, Options).Admit(caller, caller, new CalleePair("T::Score(int)", old, @new)));
    }

    /// <summary>
    /// A callee writing a field the caller reads back: the heap map's agreement is falsified and dropped, the predicate on
    /// the field read after the call stays, and a map only the callee names must agree.
    /// </summary>
    [Fact]
    public void AHeapCellTheCallWroteIsObservedThroughItsPredicate()
    {
        (IrProcedure caller, _) = Fixture.Pair(FieldCaller + "\n---\n" + FieldCaller);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Bump(C)" (ref %field.C.x: map<sort "C", bv32>, ref %field.C.y: map<sort "C", bv32>, %this: sort "C") entry B0
            B0:
              %one: bv32 = const bv32 1
              %x1: map<sort "C", bv32> = mapwrite %field.C.x, %this, %one
              ret outs(%field.C.x = %x1, %field.C.y = %field.C.y)
            ---
            proc "T::Bump(C)" (ref %field.C.x: map<sort "C", bv32>, ref %field.C.y: map<sort "C", bv32>, %this: sort "C") entry B0
            B0:
              %two: bv32 = const bv32 2
              %x1: map<sort "C", bv32> = mapwrite %field.C.x, %this, %two
              ret outs(%field.C.x = %x1, %field.C.y = %field.C.y)
            """);
        ContractSearch search = new(Context, Options);
        CalleePair callee = new("T::Bump(C)", old, @new);

        CalleeContract candidate = search.Candidate(caller, caller, callee);
        CalleeContract admitted = search.Admit(caller, caller, callee)!;

        Assert.Equal(
            [(ConjunctKind.Heap, "field.C.x", true), (ConjunctKind.Heap, "field.C.y", false)],
            candidate.Conjuncts.Where(static c => c.Kind == ConjunctKind.Heap).Select(static c => (c.Kind, c.Map!.Name, c.InCaller)));
        Assert.DoesNotContain(admitted.Conjuncts, static c => c is { Kind: ConjunctKind.Heap, InCaller: true });
        Assert.Contains(admitted.Conjuncts, static c => c.Kind == ConjunctKind.Predicate);
        Assert.Contains(admitted.Conjuncts, static c => c is { Kind: ConjunctKind.Heap, InCaller: false });
        Assert.Null(new Z3Backend().VerifyUnderContracts(caller, caller, [callee], Options));
    }

    [Fact]
    public void OnlyConjunctsTheCallerSeesFailAreDroppable()
    {
        TraceEncoder.HeapMap map = new("field.C.x", new IrMap(new IrSort("C"), new IrBitVec(32)));

        Assert.True(Threw.Droppable);
        Assert.True(new ContractConjunct(ConjunctKind.Predicate, Predicate: Positive(new IrBitVec(32))).Droppable);
        Assert.True(new ContractConjunct(ConjunctKind.Heap, map).Droppable);
        Assert.False(new ContractConjunct(ConjunctKind.Heap, map, InCaller: false).Droppable);
        Assert.False(new ContractConjunct(ConjunctKind.ExceptionType).Droppable);
        Assert.False(new ContractConjunct(ConjunctKind.Calls).Droppable);
    }

    [Fact]
    public void ObservedPredicatesFollowOnlyTheCallsOutputsConstantsAndUnchangingInputs()
    {
        IrProcedure caller = IrText.Parse("""
            proc "T::C(string, int)" (%s: sort "string", %n: bv32, %null.string: map<sort "string", bool>) -> bool entry B0
            B0:
              %r: sort "string" = call "T::F(string)"(%s) threw %t: bool
              %isnull: bool = mapread %null.string, %r
              %ok: bool = call "T::G(string)"(%s) threw %u: bool
              %flag: bool = call "T::F2(string)"(%s) threw %v: bool
              %k: bv32 = call "T::F3(string)"(%s) threw %w: bool
              %mixed: bool = slt %k, %n
              %notnull: bool = boolnot %isnull
              opaque "Unsupported" at "C.cs" 1:1-1:2
              ret %notnull
            """);

        ObservedPredicate[] ofF = [.. ObservedPredicates.Of(caller, static c => string.Equals(c.Callee.Value, "T::F(string)", StringComparison.Ordinal))];
        ObservedPredicate[] ofF2 = [.. ObservedPredicates.Of(caller, static c => string.Equals(c.Callee.Value, "T::F2(string)", StringComparison.Ordinal))];
        ObservedPredicate[] ofF3 = [.. ObservedPredicates.Of(caller, static c => string.Equals(c.Callee.Value, "T::F3(string)", StringComparison.Ordinal))];

        Assert.Equal("isnull notnull", string.Join(' ', ofF.Select(static p => p.Output.Name)));
        Assert.Equal(
            [PredicateLeafKind.Result, PredicateLeafKind.Input],
            ofF[0].Leaves.Values.Select(static l => l.Kind).Order());
        Assert.Equal("flag", Assert.Single(ofF2).Output.Name);
        Assert.Empty(ofF2[0].Slice);
        Assert.Empty(ofF3);
        using Context context = new();
        SortMapper sorts = new(context);
        Expr notNull = ofF[1].Encode(context, sorts, leaf => context.MkConst(leaf.Kind == PredicateLeafKind.Result ? "r" : leaf.Name, sorts.Sort(leaf.Type)))!;
        Assert.Equal("(not (select null.string r))", notNull.ToString());
    }

    [Fact]
    public void AnObservedPredicateWithoutATermForALeafHasNone()
    {
        using Context context = new();
        SortMapper sorts = new(context);
        ObservedPredicate positive = Positive(new IrBitVec(32));

        Assert.Null(positive.Encode(context, sorts, static _ => null));
        Assert.Null(positive.Encode(context, sorts, _ => context.MkBVConst("wide", 64)));
        Assert.Equal("(bvsgt r #x00000000)", positive.Encode(context, sorts, _ => context.MkBVConst("r", 32))!.ToString());
    }

    /// <summary>
    /// In a caller's product a call to a callee without a contract keeps its shared functions, and a conjunct with nothing
    /// to relate is left out: a call whose result is discarded on one side,
    /// a call on a path never taken whose arguments differ in type, or a heap map the caller does not name.
    /// </summary>
    [Theory]
    [InlineData("""
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          call "T::Log()"() threw %l: bool
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          %p: bool = sgt %r, %zero
          ret %zero
        ---
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          call "T::Log()"() threw %l: bool
          call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          ret %zero
        """)]
    [InlineData("""
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          ret %zero
        ---
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          %p: bool = sgt %r, %zero
          ret %zero
        """)]
    [InlineData("""
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %never: bool = const bool false
          br %never, B1, B2
        B1:
          %w: bv64 = sext %a
          call "T::Score(int)"(%w) threw %u: bool
          goto B2
        B2:
          call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          ret %zero
        ---
        proc "T::C(int)" (%a: bv32) -> bv32 entry B0
        B0:
          call "T::Score(int)"(%a) threw %t: bool
          %zero: bv32 = const bv32 0
          ret %zero
        """)]
    public void ACallerRelatesOnlyWhatBothCallsHave(string callers)
    {
        ArgumentNullException.ThrowIfNull(callers);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(callers);
        (IrProcedure scoreOld, IrProcedure scoreNew) = Fixture.Pair(Score);
        TraceEncoder.HeapMap unnamed = new("field.C.x", new IrMap(new IrSort("C"), new IrBitVec(32)));
        CalleeContract contract = new(
        [
            Threw,
            new ContractConjunct(ConjunctKind.Heap, unnamed, InCaller: false),
            new ContractConjunct(ConjunctKind.Predicate, Predicate: Positive(new IrBitVec(32))),
        ]);
        Assert.IsType<ContractCheck.Admitted>(Verifier().Verify(scoreOld, scoreNew, contract));

        Verdict verdict = new LoopLadder(Context, Options) { Contracts = Contracts(contract) }.Verify(old, @new);

        Assert.IsType<Equivalent>(verdict);
    }

    /// <summary>
    /// In a callee pair's product a result leaf of a callee that returns nothing leaves its conjunct <c>true</c>, and a map
    /// or an input the callee does not name is one constant for both sides.
    /// </summary>
    [Fact]
    public void ACalleeWithoutWhatAConjunctNamesSatisfiesIt()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::V()" () entry B0
            B0:
              ret
            ---
            proc "T::V()" () entry B0
            B0:
              ret
            """);
        IrMap map = new(new IrSort("C"), new IrBitVec(32));
        IrVar h = new("h", map);
        IrVar @this = new("this", new IrSort("C"));
        IrVar v = new("v", new IrBitVec(32));
        IrVar zero = new("zero", new IrBitVec(32));
        IrVar p = new("p", new IrBool());
        ObservedPredicate field = new(
            [new IrMapRead(v, h, @this), new IrConst(zero, IrBitVecValue.FromSigned(32, 0)), new IrBinary(p, IrBinaryOp.Sgt, v, zero)],
            p,
            ImmutableDictionary.CreateRange(StringComparer.Ordinal, [
                KeyValuePair.Create("h", new PredicateLeaf(PredicateLeafKind.Heap, "field.C.x", map)),
                KeyValuePair.Create("this", new PredicateLeaf(PredicateLeafKind.Input, "this", @this.Type)),
            ]));
        CalleeContract contract = new(
        [
            new ContractConjunct(ConjunctKind.Predicate, Predicate: Positive(new IrBitVec(32))),
            new ContractConjunct(ConjunctKind.Predicate, Predicate: field),
            new ContractConjunct(ConjunctKind.Heap, new TraceEncoder.HeapMap("field.C.x", map)),
        ]);

        Assert.Equal(new ContractCheck.Admitted(ProofMethod.Bounded), Verifier().Verify(old, @new, contract));
    }

    private const string Equals11 = """
        proc "T::Eleven(int)" (%a: bv32) -> bool entry B0
        B0:
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          %eleven: bv32 = const bv32 11
          %p: bool = eq %r, %eleven
          ret %p
        """;

    private const string Both = """
        proc "T::Both(int)" (%a: bv32) -> bool entry B0
        B0:
          %r: bv32 = call "T::Score(int)"(%a) threw %t: bool
          %eleven: bv32 = const bv32 11
          %zero: bv32 = const bv32 0
          %p: bool = eq %r, %eleven
          %q: bool = sgt %r, %zero
          %pq: bool = and %p, %q
          ret %pq
        """;

    private const string FieldCaller = """
        proc "T::Read(C)" (ref %field.C.x: map<sort "C", bv32>, %this: sort "C") -> bool entry B0
        B0:
          call "T::Bump(C)"(%this) threw %t: bool heap("field.C.x" %field.C.x -> %x1: map<sort "C", bv32>)
          %v: bv32 = mapread %x1, %this
          %zero: bv32 = const bv32 0
          %p: bool = sgt %v, %zero
          ret %p outs(%field.C.x = %x1)
        """;

    private static ContractVerifier Verifier() => new(Context, Options);

    private static Context Context() => new();

    private static CallerContracts Contracts(CalleeContract contract) =>
        new(ImmutableDictionary<string, CalleeContract>.Empty.Add("T::Score(int)", contract), FreshPerSideEncoding.Instance);

    /// <summary>The candidate <paramref name="caller"/>'s use of <paramref name="callee"/> gives.</summary>
    private static CalleeContract Candidate(string caller, string callee)
    {
        IrProcedure body = IrText.Parse(caller);
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(callee);
        return new ContractSearch(Context, Options).Candidate(body, body, new CalleePair(old.Identity.Value, old, @new));
    }

    /// <summary><c>r &gt; 0</c> over a result of <paramref name="type"/>.</summary>
    private static ObservedPredicate Positive(IrBitVec type)
    {
        IrVar r = new("r", type);
        IrVar zero = new("zero", type);
        IrVar p = new("p", new IrBool());
        return new ObservedPredicate(
            [new IrConst(zero, IrBitVecValue.FromSigned(type.Width, 0)), new IrBinary(p, IrBinaryOp.Sgt, r, zero)],
            p,
            ImmutableDictionary<string, PredicateLeaf>.Empty.Add("r", new PredicateLeaf(PredicateLeafKind.Result, string.Empty, type)));
    }

    /// <summary>ADR 0036's rejected encoding, built here only: the shared call functions, with K asserted on top.</summary>
    private sealed class SharedFunctionEncoding : ICalleeContractEncoding
    {
        public bool FreshPerSide => false;
    }
}
