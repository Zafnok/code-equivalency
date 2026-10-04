using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Equiv.Verify.Z3.Conditions;

using Microsoft.Z3;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// ADR 0048's input condition (ticket P1-022): the candidates harvested from a pair's bodies, the two checks that admit
/// one, and the condition a Divergent or an abstraction Unknown then carries.
/// </summary>
public sealed class ConditionTests
{
    private const string Guarded = """
        proc "T::Greet(string)" (%name: sort "string", %null.string: map<sort "string", bool>) -> sort "string" entry B0
        B0:
          %n: bool = mapread %null.string, %name
          br %n, B1, B2
        B1:
          throw "System.ArgumentNullException"
        B2:
          %r: sort "string" = call "System.String::ToUpper()"(%name)
          ret %r
        """;

    private const string Unguarded = """
        proc "T::Greet(string)" (%who "who": sort "string", %null.string: map<sort "string", bool>) -> sort "string" entry B0
        B0:
          %n: bool = mapread %null.string, %who
          br %n, B1, B2
        B1:
          throw "System.NullReferenceException"
        B2:
          %r: sort "string" = call "System.String::ToUpper()"(%who)
          ret %r
        """;

    private static readonly VerificationOptions Options = new(3, 10_000, []);

    private static readonly IrType Bv32 = new IrBitVec(32);

    /// <summary>
    /// The <c>removed-null-check</c> shape: the pair diverges only on a null argument, so it is proved Equivalent when the
    /// argument is not null. The text has the modern side's parameter name, and the SMT-LIB the product's input names.
    /// </summary>
    [Fact]
    public void RemovedGuard_AgreesWhenTheArgumentIsNotNull()
    {
        Divergent divergent = Assert.IsType<Divergent>(new Z3Backend().Verify(IrText.Parse(Guarded), IrText.Parse(Unguarded), Options));

        ConditionSearch search = Assert.IsType<ConditionSearch>(divergent.Conditions);
        Assert.Equal(new AgreesWhen("(not (select in.null.string in.name))", "who != null"), search.AgreesWhen);
        Assert.False(search.Contradicted);
        Assert.True(search.Elapsed > TimeSpan.Zero);
    }

    /// <summary>
    /// Only a Bool value that reaches source parameters both sides have, a null shadow of one, and constants, through binary
    /// and unary operations, is a candidate. A call's result, a field read, a pure function, a phi, a parameter one side
    /// lacks, a synthesised input, a truncation, a written map, a sort literal, a value of no input and one of three inputs
    /// are not.
    /// </summary>
    [Fact]
    public void Candidates_AreOnlyPredicatesOverSharedInputs()
    {
        IrProcedure old = IrText.Parse("""
            proc "T::M(int,int,int,bool,string)" (%a: bv32, %b: bv32, %c: bv32, %flag: bool, %s: sort "string", %extra: bv32, %field.T.f: map<sort "string", bv32>, %null.string: map<sort "string", bool>, %this: sort "T") -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %t: bool = const bool true
              %neg: bool = slt %a, %z
              %notNeg: bool = boolnot %neg
              %sum: bv32 = add %a, %b
              %sumPos: bool = sgt %sum, %z
              %three: bv32 = add %sum, %c
              %threePos: bool = sgt %three, %z
              %constOnly: bool = eq %z, %z
              %called: bv32 = call "T::F()"()
              %calledPos: bool = sgt %called, %z
              %read: bv32 = mapread %field.T.f, %s
              %readPos: bool = sgt %read, %z
              %pure: bv32 = pure "f64.add"(%a, %b)
              %purePos: bool = sgt %pure, %z
              %oneSided: bool = sgt %extra, %z
              %small: bv8 = trunc %a
              %z8: bv8 = const bv8 0
              %smallPos: bool = sgt %small, %z8
              %isNull: bool = mapread %null.string, %s
              %written: map<sort "string", bool> = mapwrite %null.string, %s, %t
              %afterWrite: bool = mapread %written, %s
              %lit: sort "string" = const sort "string" 1
              %isLit: bool = eq %s, %lit
              %thisSame: bool = eq %this, %this
              %both: bool = and %flag, %t
              br %flag, B1, B2
            B1:
              goto B3
            B2:
              goto B3
            B3:
              %phi: bv32 = phi [B1: %a, B2: %b]
              %phiPos: bool = sgt %phi, %z
              ret %z
            """);
        IrProcedure @new = IrText.Parse("""
            proc "T::M(int,int,int,bool,string)" (%x "x": bv32, %b: bv32, %c: bv32, %flag: bool, %s: sort "string", %field.T.f: map<sort "string", bv32>, %null.string: map<sort "string", bool>, %this: sort "T") -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %neg: bool = slt %x, %z
              %keyed: sort "string" = call "T::G()"()
              %keyedNull: bool = mapread %null.string, %keyed
              ret %z
            """);
        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(old, @new);

        ImmutableArray<ConditionTerm> candidates = CandidateHarvest.Of(old, @new, shared);

        Assert.Equal(
            ["flag", "!flag", "x < 0", "x >= 0", "s == null", "s != null", "flag & true", "!(flag & true)", "(x + b) > 0", "(x + b) <= 0"],
            candidates.Select(c => ConditionText.Source(c, shared)),
            StringComparer.Ordinal);
    }

    /// <summary>At most sixteen candidates, the shallowest values first; each value is counted once however often it is computed.</summary>
    [Fact]
    public void Candidates_AreCappedAtSixteen_ShallowestFirst()
    {
        StringBuilder body = new();
        body.AppendLine("""proc "T::M(int)" (%a: bv32) -> bv32 entry B0""").AppendLine("B0:").AppendLine("  %one: bv32 = const bv32 1").AppendLine("  %deep: bv32 = add %a, %one");
        body.AppendLine("  %deepPos: bool = sgt %deep, %one").AppendLine("  %again: bool = sgt %deep, %one");
        for (int i = 0; i < 9; i++)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  %k{i}: bv32 = const bv32 {i}").AppendLine(CultureInfo.InvariantCulture, $"  %c{i}: bool = slt %a, %k{i}");
        }

        IrProcedure procedure = IrText.Parse(body.AppendLine("  ret %a").ToString());
        ImmutableArray<SharedParameter> shared = ProductEncoder.Pair(procedure, procedure);

        ImmutableArray<ConditionTerm> candidates = CandidateHarvest.Of(procedure, procedure, shared);

        Assert.Equal(CandidateHarvest.MaxCandidates, candidates.Length);
        Assert.Equal(16, CandidateHarvest.MaxCandidates);
        Assert.Equal(
            Enumerable.Range(0, 8).Select(static i => i.ToString(CultureInfo.InvariantCulture)).SelectMany(static i => new[] { $"a < {i}", $"a >= {i}" }),
            candidates.Select(c => ConditionText.Source(c, shared)),
            StringComparer.Ordinal);
    }

    /// <summary>A value taller than <see cref="CandidateHarvest.MaxDepth"/> is not harvested; one exactly that tall is.</summary>
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public void Candidates_AreNoTallerThanTheDepthLimit(int additions, bool harvested)
    {
        StringBuilder body = new();
        body.AppendLine("""proc "T::M(int)" (%a: bv32) -> bv32 entry B0""").AppendLine("B0:").AppendLine("  %v0: bv32 = add %a, %a");
        for (int i = 1; i < additions; i++)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"  %v{i}: bv32 = add %v{i - 1}, %a");
        }

        IrProcedure procedure = IrText.Parse(body.AppendLine(CultureInfo.InvariantCulture, $"  %c: bool = eq %v{additions - 1}, %a").AppendLine("  ret %a").ToString());

        ImmutableArray<ConditionTerm> candidates = CandidateHarvest.Of(procedure, procedure, ProductEncoder.Pair(procedure, procedure));

        Assert.Equal(harvested ? 2 : 0, candidates.Length);
        Assert.All(candidates, static c => Assert.True(c.Depth <= CandidateHarvest.MaxDepth + 1));
        Assert.Equal(8, CandidateHarvest.MaxDepth);
    }

    /// <summary>
    /// On the inputs where <c>a &lt; 0</c> both sides return the same value, but one side reaches an unshared opaque node
    /// first, so its outcome there is unknown (ADR 0014) and the candidate is not admitted. Either side may be the one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Candidate_IsRejectedWhenAnOpaqueIsReachable(bool opaqueOnNewSide)
    {
        string opaque = Branching("""
              %o: bv32 = opaque "Await" at "Side.cs" 3:9-3:30
              ret %z
            """, "1");
        string plain = Branching("  ret %z", "2");

        Divergent divergent = Assert.IsType<Divergent>(opaqueOnNewSide ? Verify(plain, opaque) : Verify(opaque, plain));

        Assert.Equal(new ConditionSearch(AgreesWhen: null), divergent.Conditions);

        static string Branching(string negative, string otherwise) => $"""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              br %c, B1, B2
            B1:
            {negative}
            B2:
              %r: bv32 = const bv32 {otherwise}
              ret %r
            """;
    }

    /// <summary>
    /// <c>a &lt; a</c> holds on no input, so "the pair agrees whenever it holds" is true and says nothing: the second check
    /// rejects it. Its negation holds on every input, where the pair diverges.
    /// </summary>
    [Fact]
    public void Candidate_IsRejectedWhenVacuous()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Returning("1"), Returning("2")));

        Assert.Equal(new ConditionSearch(AgreesWhen: null), divergent.Conditions);

        static string Returning(string value) => $"""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %never: bool = slt %a, %a
              %r: bv32 = const bv32 {value}
              ret %r
            """;
    }

    /// <summary>The pair diverges only when neither argument is negative: each of the two guards is admitted alone, and the condition is their disjunction.</summary>
    [Fact]
    public void AgreesWhen_IsTheDisjunctionOfAdmittedCandidates()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Guards("1"), Guards("2")));

        Assert.Equal(
            new AgreesWhen("(or (bvslt in.a (_ bv0 32)) (bvslt in.b (_ bv0 32)))", "a < 0 || b < 0"),
            divergent.Conditions?.AgreesWhen);

        static string Guards(string otherwise) => $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              br %c, B1, B2
            B1:
              ret %z
            B2:
              %d: bool = slt %b, %z
              br %d, B1, B3
            B3:
              %r: bv32 = const bv32 {otherwise}
              ret %r
            """;
    }

    /// <summary>
    /// The pair diverges only from <paramref name="values"/> up, so every <c>a &lt; k</c> below is admitted, and each
    /// implies the next. Up to six admitted, only the weakest is reported. Above six, no implication is checked.
    /// </summary>
    [Theory]
    [InlineData(2, "a < 2")]
    [InlineData(6, "a < 6")]
    [InlineData(7, "a < 1 || a < 2 || a < 3 || a < 4 || a < 5 || a < 6 || a < 7")]
    public void AgreesWhen_DropsAnImpliedCandidate(int values, string text)
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Thresholds("10"), Thresholds("20")));

        Assert.Equal(text, divergent.Conditions?.AgreesWhen?.Text);
        Assert.Equal(6, ConditionQuery.MaxImplicationChecked);

        string Thresholds(string otherwise)
        {
            StringBuilder body = new();
            body.AppendLine("""proc "T::M(int)" (%a: bv32) -> bv32 entry B0""").AppendLine("B0:");
            for (int i = 1; i <= values; i++)
            {
                body.AppendLine(CultureInfo.InvariantCulture, $"  %k{i}: bv32 = const bv32 {i}").AppendLine(CultureInfo.InvariantCulture, $"  %c{i}: bool = slt %a, %k{i}");
            }

            return body
                .AppendLine(CultureInfo.InvariantCulture, $"  br %c{values}, B1, B2")
                .AppendLine("B1:").AppendLine("  ret %k1")
                .AppendLine("B2:").AppendLine(CultureInfo.InvariantCulture, $"  %r: bv32 = const bv32 {otherwise}").AppendLine("  ret %r")
                .ToString();
        }
    }

    /// <summary>Two admitted candidates that imply each other are one condition, reported once.</summary>
    [Fact]
    public void AgreesWhen_KeepsOneOfTwoEquivalentCandidates()
    {
        Divergent divergent = Assert.IsType<Divergent>(Verify(Spelled("1"), Spelled("2")));

        Assert.Equal("0 > a", divergent.Conditions?.AgreesWhen?.Text);

        static string Spelled(string otherwise) => $"""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              %same: bool = sgt %z, %a
              br %c, B1, B2
            B1:
              ret %z
            B2:
              %r: bv32 = const bv32 {otherwise}
              ret %r
            """;
    }

    /// <summary>
    /// The reported counterexample makes the admitted condition false. One that makes it true contradicts the proof: then
    /// nothing is reported, and the search says so (ADR 0048 decision 7).
    /// </summary>
    [Theory]
    [InlineData(5, false)]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public void AgreesWhen_IsFalsifiedByTheCounterexample(int argument, bool contradicted)
    {
        IrProcedure old = IrText.Parse(Negative("1"));
        IrProcedure @new = IrText.Parse(Negative("2"));
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, old, @new, []);
        IrRun run = new(new IrReturned(new IrBitVecValue(32, 0)), [], []);
        Counterexample counterexample = new(new IrInputs([IrBitVecValue.FromSigned(32, argument)]), run, run);

        ConditionSearch search = new ConditionQuery(static () => new Context(), Options).Search(
            context, encoding, CandidateHarvest.Of(old, @new, ProductEncoder.Pair(old, @new)), counterexample);

        Assert.Equal(contradicted ? new ConditionSearch(AgreesWhen: null) { Contradicted = true } : new ConditionSearch(new AgreesWhen("(bvslt in.a (_ bv0 32))", "a < 0")), search);

        Divergent divergent = Assert.IsType<Divergent>(Verify(Negative("1"), Negative("2")));
        Assert.Equal(new ConditionSearch(new AgreesWhen("(bvslt in.a (_ bv0 32))", "a < 0")), divergent.Conditions);
        Assert.True(Assert.IsType<IrBitVecValue>(divergent.Counterexample.Inputs.Arguments[0]).TwosComplement >= 0);
    }

    /// <summary>A sort-valued counterexample is told apart from the other elements of its sort: the null argument falsifies <c>name != null</c>.</summary>
    [Fact]
    public void AgreesWhen_IsFalsifiedByACounterexampleOverASort()
    {
        IrProcedure old = IrText.Parse(Guarded);
        IrProcedure @new = IrText.Parse(Unguarded);
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, old, @new, []);
        ImmutableArray<ConditionTerm> candidates = CandidateHarvest.Of(old, @new, ProductEncoder.Pair(old, @new));
        IrRun run = new(new IrThrew("System.Exception"), [], []);
        IrMap nullness = new(new IrSort("string"), new IrBool());

        ConditionSearch Search(int argument) => new ConditionQuery(static () => new Context(), Options).Search(
            context,
            encoding,
            candidates,
            new Counterexample(
                new IrInputs([new IrSortValue("string", argument), new IrMapValue(nullness, new IrBoolValue(Value: false), ImmutableDictionary<IrValue, IrValue>.Empty.Add(new IrSortValue("string", 1), new IrBoolValue(Value: true)))]),
                run,
                run));

        Assert.NotNull(Search(1).AgreesWhen);
        Assert.Equal(new ConditionSearch(AgreesWhen: null) { Contradicted = true }, Search(2));
    }

    /// <summary>Rung 1's product proves nothing past the bound, so a pair with a loop or a self-call on either side is not searched.</summary>
    [Theory]
    [InlineData("3", "4", false)]
    [InlineData("3", "4", true)]
    public void LoopingPair_HasNoAgreesWhen(string oldStep, string newStep, bool swapped)
    {
        static string Loop(string step) => $"""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %s: bv32 = const bv32 {step}
              goto B1
            B1:
              %i: bv32 = phi [B0: %a, B2: %next]
              %c: bool = slt %i, %z
              br %c, B2, B3
            B2:
              %next: bv32 = add %i, %s
              goto B1
            B3:
              ret %i
            """;
        string straight = $"""
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              %r: bv32 = add %a, %a
              ret %r
            """;

        Divergent looping = Assert.IsType<Divergent>(Verify(Loop(oldStep), Loop(newStep)));
        Divergent oneSided = Assert.IsType<Divergent>(swapped ? Verify(straight, Loop(newStep)) : Verify(Loop(oldStep), straight));

        Assert.Null(looping.Conditions);
        Assert.Null(oneSided.Conditions);
    }

    /// <summary>A side that calls itself is not searched either.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfCallingPair_HasNoAgreesWhen(bool swapped)
    {
        const string Recursive = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = sle %a, %z
              br %c, B1, B2
            B1:
              ret %z
            B2:
              %one: bv32 = const bv32 1
              %less: bv32 = sub %a, %one
              %r: bv32 = call "T::M(int)"(%less) threw %t: bool
              ret %r
            """;
        const string Constant = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = sle %a, %z
              %five: bv32 = const bv32 5
              ret %five
            """;

        Verdict verdict = swapped ? Verify(Constant, Recursive) : Verify(Recursive, Constant);

        Assert.Null(Assert.IsType<Divergent>(verdict).Conditions);
    }

    /// <summary>
    /// A divergence whose model calls a runtime-changed member is EQ006. That callee's functions are each side's own, so
    /// the pair is not searched, whichever side's trace holds the call.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeChangedDivergence_IsNotSearched(bool swapped)
    {
        const string Calls = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              %r: bv32 = call "System.String::GetHashCode()"!(%a)
              ret %r
            """;
        const string Returns = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              ret %z
            """;

        Verdict verdict = swapped ? Verify(Returns, Calls) : Verify(Calls, Returns);

        Assert.Null(Assert.IsType<Divergent>(verdict).Conditions);
    }

    /// <summary>
    /// An Unknown whose divergence rests on a shared fragment read with different arguments carries the condition too: the
    /// fragment answers alike when the arguments are equal. The verdict stays Unknown.
    /// </summary>
    [Fact]
    public void AbstractionUnknown_CarriesAgreesWhen()
    {
        static string Reads(string argument) => $"""
            proc "T::M(int,int)" (%a: bv32, %b: bv32) -> bv32 entry B0
            B0:
              %same: bool = eq %a, %b
              %r: bv32 = opaque "DelegateCreation" at "Side.cs" 3:9-3:30 fragment "f1" reads(%{argument})
              ret %r
            """;

        Unknown unknown = Assert.IsType<Unknown>(Verify(Reads("a"), Reads("b")));

        Assert.Equal(UnknownReason.Abstraction, unknown.Reason);
        Assert.Equal(new AgreesWhen("(= in.a in.b)", "a == b"), unknown.Conditions?.AgreesWhen);
        Assert.NotNull(unknown.FailureRefinement);
    }

    /// <summary>An Equivalent has nothing to search, and neither has an Unknown for any reason but an abstraction.</summary>
    [Fact]
    public void OtherVerdicts_AreNotSearched()
    {
        const string Opaque = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              %o: bv32 = opaque "Await" at "Side.cs" 3:9-3:30
              ret %o
            """;
        const string Plain = """
            proc "T::M(int)" (%a: bv32) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = slt %a, %z
              ret %z
            """;

        Assert.IsType<Equivalent>(Verify(Plain, Plain));
        Unknown unknown = Assert.IsType<Unknown>(Verify(Opaque, Plain));
        Assert.Equal(UnknownReason.Opaque, unknown.Reason);
        Assert.Null(unknown.Conditions);
    }

    /// <summary>A query the solver gives up on admits nothing: the search ran and found no condition.</summary>
    [Fact]
    public void SolverGivesUp_AdmitsNothing()
    {
        IrProcedure old = IrText.Parse(Guarded);
        IrProcedure @new = IrText.Parse(Unguarded);
        IrRun run = new(new IrThrew("System.Exception"), [], []);
        Counterexample counterexample = new(new IrInputs([]), run, run);

        ConditionSearch? search = new ConditionQuery(static () => new Context(), Options with { ResourceLimit = 1 }).Run(old, @new, counterexample);

        Assert.Equal(new ConditionSearch(AgreesWhen: null), search);
        Assert.Null(new ConditionQuery(static () => new Context(), Options).Run(old, @new, counterexample: null));
    }

    /// <summary>A predicate through every unary operation is encoded as the body's own value is: the condition it gives is proved on the product.</summary>
    [Fact]
    public void UnaryOperations_AreEncodedAsTheBodyEncodesThem()
    {
        static string Widened(string otherwise) => $"""
            proc "T::M(sbyte,byte)" (%a: bv8, %b: bv8) -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %wide: bv32 = sext %a
              %minus: bv32 = neg %wide
              %c: bool = sgt %minus, %z
              br %c, B1, B2
            B1:
              ret %z
            B2:
              %unsigned: bv32 = zext %b
              %flipped: bv32 = not %unsigned
              %big: bv32 = const bv32 4294967040
              %d: bool = ugt %flipped, %big
              br %d, B1, B3
            B3:
              %r: bv32 = const bv32 {otherwise}
              ret %r
            """;

        Divergent divergent = Assert.IsType<Divergent>(Verify(Widened("1"), Widened("2")));

        Assert.Equal(
            new AgreesWhen(
                "(or (bvsgt (bvneg ((_ sign_extend 24) in.a)) (_ bv0 32)) (bvugt (bvnot ((_ zero_extend 24) in.b)) (_ bv4294967040 32)))",
                "-a > 0 || (uint)~(byte)b > 4294967040"),
            divergent.Conditions?.AgreesWhen);
    }

    /// <summary>Both renderings of each kind of term: SMT-LIB over the product's inputs, and the source spelling with the modern names.</summary>
    [Theory]
    [MemberData(nameof(Renderings))]
    public void Terms_RenderAsSmtAndAsSource(string smt, string source, int term)
    {
        ImmutableArray<SharedParameter> shared =
        [
            Shared("a", "x", Bv32),
            Shared("flag", "flag", new IrBool()),
            Shared("items", "items", new IrSort("int[]")),
            Shared("null.int[]", "null.int[]", new IrMap(new IrSort("int[]"), new IrBool())),
            Shared("other", "other", new IrBool()),
            Shared("wide", "wide", new IrBitVec(64)),
            Shared("tiny", "tiny", new IrBitVec(8)),
            Shared("short", "short", new IrBitVec(16)),
        ];

        Assert.Equal(smt, ConditionText.Smt([Terms[term]], shared));
        Assert.Equal(source, ConditionText.Source([Terms[term]], shared));
    }

    public static TheoryData<string, string, int> Renderings()
    {
        (string Smt, string Source)[] expected =
        [
            ("(select |in.null.int[]| in.items)", "items == null"),
            ("(not (select |in.null.int[]| in.items))", "items != null"),
            ("in.flag", "flag"),
            ("(not in.flag)", "!flag"),
            ("(not (not in.flag))", "!!flag"),
            ("(and in.flag in.other)", "flag & other"),
            ("(or in.flag true)", "flag | true"),
            ("(xor in.flag false)", "flag ^ false"),
            ("(not (and in.flag in.other))", "!(flag & other)"),
            ("(= in.flag in.other)", "flag == other"),
            ("(not (= in.flag in.other))", "flag != other"),
            ("(distinct in.a (_ bv4294967295 32))", "x != -1"),
            ("(not (distinct in.a (_ bv4294967295 32)))", "x == -1"),
            ("(bvult in.a (_ bv4294967295 32))", "(uint)x < 4294967295"),
            ("(not (bvult in.a (_ bv4294967295 32)))", "(uint)x >= 4294967295"),
            ("(not (bvule in.a (_ bv1 32)))", "(uint)x > 1"),
            ("(not (bvugt in.a (_ bv1 32)))", "(uint)x <= 1"),
            ("(not (bvuge in.a (_ bv1 32)))", "(uint)x < 1"),
            ("(not (bvsle in.a (_ bv1 32)))", "x > 1"),
            ("(not (bvsgt in.a (_ bv1 32)))", "x <= 1"),
            ("(not (bvsge in.a (_ bv1 32)))", "x < 1"),
            ("(= (bvand (bvor (bvxor in.a (_ bv1 32)) (_ bv1 32)) (_ bv1 32)) (_ bv1 32))", "(((x ^ 1) | 1) & 1) == 1"),
            ("(= (bvsub (bvmul (bvadd in.a (_ bv1 32)) (_ bv1 32)) (_ bv1 32)) (_ bv1 32))", "(((x + 1) * 1) - 1) == 1"),
            ("(= (bvsrem (bvsdiv in.a (_ bv4294967295 32)) (_ bv4294967295 32)) (_ bv1 32))", "((x / -1) % -1) == 1"),
            ("(= (bvurem (bvudiv in.a (_ bv4294967295 32)) (_ bv4294967295 32)) (_ bv1 32))", "((uint)((uint)x / 4294967295) % 4294967295) == 1"),
            ("(= (bvlshr (bvashr (bvshl in.a (_ bv1 32)) (_ bv4294967295 32)) (_ bv4294967295 32)) (_ bv1 32))", "(((x << 1) >> -1) >>> -1) == 1"),
            ("(= ((_ sign_extend 32) in.a) ((_ zero_extend 32) in.a))", "x == (uint)x"),
            ("(bvult in.wide ((_ zero_extend 56) in.tiny))", "(ulong)wide < (ulong)(byte)tiny"),
            ("(bvuge ((_ zero_extend 48) in.short) ((_ zero_extend 32) in.a))", "(ulong)(ushort)short >= (ulong)(uint)x"),
            ("(= (bvshl in.wide ((_ zero_extend 32) in.a)) (bvashr in.wide ((_ sign_extend 32) in.a)))", "(wide << x) == (wide >> x)"),
            ("(= (bvlshr in.wide ((_ zero_extend 32) in.a)) (bvadd in.wide ((_ zero_extend 32) in.a)))", "(wide >>> x) == (wide + (uint)x)"),
        ];
        TheoryData<string, string, int> data = [];
        for (int i = 0; i < expected.Length; i++)
        {
            data.Add(expected[i].Smt, expected[i].Source, i);
        }

        return data;
    }

    private static ImmutableArray<ConditionTerm> Terms
    {
        get
        {
            ConditionTerm.Input a = new(0, Bv32);
            ConditionTerm.Input flag = new(1, new IrBool());
            ConditionTerm.Input items = new(2, new IrSort("int[]"));
            ConditionTerm.Input other = new(4, new IrBool());
            ConditionTerm one = new ConditionTerm.Constant(new IrBitVecValue(32, 1));
            ConditionTerm minusOne = new ConditionTerm.Constant(IrBitVecValue.FromSigned(32, -1));
            ConditionTerm isNull = new ConditionTerm.Null(3, items);
            ConditionTerm both = Binary(IrBinaryOp.And, flag, other);
            ConditionTerm Compare(IrBinaryOp op) => new ConditionTerm.Not(Binary(op, a, one));
            ConditionTerm EqualsOne(ConditionTerm value) => Binary(IrBinaryOp.Eq, value, one);
            IrType bv64 = new IrBitVec(64);
            return
            [
                isNull,
                new ConditionTerm.Not(isNull),
                flag,
                new ConditionTerm.Not(flag),
                new ConditionTerm.Not(new ConditionTerm.Not(flag)),
                both,
                Binary(IrBinaryOp.Or, flag, new ConditionTerm.Constant(new IrBoolValue(Value: true))),
                Binary(IrBinaryOp.Xor, flag, new ConditionTerm.Constant(new IrBoolValue(Value: false))),
                new ConditionTerm.Not(both),
                Binary(IrBinaryOp.Eq, flag, other),
                new ConditionTerm.Not(Binary(IrBinaryOp.Eq, flag, other)),
                Binary(IrBinaryOp.Ne, a, minusOne),
                new ConditionTerm.Not(Binary(IrBinaryOp.Ne, a, minusOne)),
                Binary(IrBinaryOp.Ult, a, minusOne),
                new ConditionTerm.Not(Binary(IrBinaryOp.Ult, a, minusOne)),
                Compare(IrBinaryOp.Ule),
                Compare(IrBinaryOp.Ugt),
                Compare(IrBinaryOp.Uge),
                Compare(IrBinaryOp.Sle),
                Compare(IrBinaryOp.Sgt),
                Compare(IrBinaryOp.Sge),
                EqualsOne(Binary(IrBinaryOp.And, Binary(IrBinaryOp.Or, Binary(IrBinaryOp.Xor, a, one), one), one)),
                EqualsOne(Binary(IrBinaryOp.Sub, Binary(IrBinaryOp.Mul, Binary(IrBinaryOp.Add, a, one), one), one)),
                EqualsOne(Binary(IrBinaryOp.SRem, Binary(IrBinaryOp.SDiv, a, minusOne), minusOne)),
                EqualsOne(Binary(IrBinaryOp.URem, Binary(IrBinaryOp.UDiv, a, minusOne), minusOne)),
                EqualsOne(Binary(IrBinaryOp.LShr, Binary(IrBinaryOp.AShr, Binary(IrBinaryOp.Shl, a, one), minusOne), minusOne)),
                Binary(IrBinaryOp.Eq, new ConditionTerm.Unary(IrUnaryOp.SExt, a, bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, a, bv64)),
                Binary(IrBinaryOp.Ult, new ConditionTerm.Input(5, bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, new ConditionTerm.Input(6, new IrBitVec(8)), bv64)),
                Binary(IrBinaryOp.Uge, new ConditionTerm.Unary(IrUnaryOp.ZExt, new ConditionTerm.Input(7, new IrBitVec(16)), bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, a, bv64)),
                Binary(
                    IrBinaryOp.Eq,
                    Binary(IrBinaryOp.Shl, new ConditionTerm.Input(5, bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, a, bv64)),
                    Binary(IrBinaryOp.AShr, new ConditionTerm.Input(5, bv64), new ConditionTerm.Unary(IrUnaryOp.SExt, a, bv64))),
                Binary(
                    IrBinaryOp.Eq,
                    Binary(IrBinaryOp.LShr, new ConditionTerm.Input(5, bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, a, bv64)),
                    Binary(IrBinaryOp.Add, new ConditionTerm.Input(5, bv64), new ConditionTerm.Unary(IrUnaryOp.ZExt, a, bv64))),
            ];
        }
    }

    private static string Negative(string otherwise) => $"""
        proc "T::M(int)" (%a: bv32) -> bv32 entry B0
        B0:
          %z: bv32 = const bv32 0
          %c: bool = slt %a, %z
          br %c, B1, B2
        B1:
          ret %z
        B2:
          %r: bv32 = const bv32 {otherwise}
          ret %r
        """;

    private static ConditionTerm.Binary Binary(IrBinaryOp op, ConditionTerm a, ConditionTerm b) => new(op, a, b);

    private static SharedParameter Shared(string old, string modern, IrType type) =>
        new(new IrParameter(new IrVar(old, type), IrParameterKind.In), new IrParameter(new IrVar("renamed", type, modern), IrParameterKind.In));

    private static Verdict Verify(string old, string @new) => new Z3Backend().Verify(IrText.Parse(old), IrText.Parse(@new), Options);
}
