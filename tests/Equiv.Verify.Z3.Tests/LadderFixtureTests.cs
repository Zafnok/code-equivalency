using System.Security.Cryptography;
using System.Text;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3.Refinement;

using Microsoft.Z3;

using VerifyXunit;

using Xunit;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3.Tests;

/// <summary>
/// Every fixture under <c>Fixtures/loops/</c> takes the verdict path its first comment line names (ticket M3-002
/// criteria 1 and 3): <c>Equivalent(&lt;proofMethod&gt;)</c>, <c>Divergent(&lt;rung that refuted it&gt;)</c> with a
/// counterexample whose replay diverges, or <c>Unknown(&lt;reason&gt;)</c>; once rung 4 ran, the arithmetic its answer
/// holds in follows (<c>, int</c> or <c>, bitvector</c>; ticket P1-001 criterion 4).
/// </summary>
public sealed class LadderFixtureTests
{
    public static TheoryData<string> Names =>
    [
        "aligned-unchanged", "loop-bound-change", "warm-up", "loop-to-linq", "recursion-unaligned",
        "loop-break-return", "loop-break-return-mutant", "loop-invariant-livein", "loop-invariant-livein-mutant",
        "nested-aligned", "nesting-changed", "late-divergence", "late-divergence-beyond", "constant-loop-prefix-change",
        "phis-reordered", "state-unpaired", "irreducible", "loop-opaque", "loop-hard",
        "recursion-aligned", "recursion-divergent", "recursion-heap", "recursion-array-divergent",
        "trip-count-changed", "chc-spurious", "chc-overflow-bitvectors", "fusion", "counter-shape", "array-count",
        "int-proof-wraps", "chc-uncertified",
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureTakesTheVerdictPathItsFirstLineNames(string name)
    {
        Fixture fixture = Fixture.Load("loops/" + name);

        Verdict verdict = Verify(fixture);

        string described = Describe(verdict);
        Assert.True(
            string.Equals(fixture.Expected, described, StringComparison.Ordinal),
            $"expected {fixture.Expected}, got {described}; ladder: {string.Join("; ", verdict.Ladder.Select(static s => $"{s.Rung} {s.Outcome}: {s.Detail}"))}");
        Assert.NotEmpty(verdict.Ladder);
        Assert.Equal(ProofMethod.Bounded, verdict.Ladder[0].Rung);
        if (verdict is Divergent divergent)
        {
            Assert.NotEqual(divergent.Counterexample.Old, divergent.Counterexample.New);
            Assert.All([divergent.Counterexample.Old, divergent.Counterexample.New], static r => Assert.IsType<IrOutcome>(r.Outcome, exactMatch: false));
            Assert.All([divergent.Counterexample.Old.Outcome, divergent.Counterexample.New.Outcome], static o => Assert.True(o is IrReturned or IrThrew, o.ToString()));
        }
    }

    [Fact]
    public void EveryFixtureFileIsListed()
    {
        IEnumerable<string> files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "loops"), "*.ir").Select(Path.GetFileNameWithoutExtension)!;

        Assert.Equal(files.Order(StringComparer.Ordinal), Names.Select(static row => row.Data).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ABoundedProofOverALoopNamesItsBound()
    {
        (IrProcedure old, IrProcedure @new) = Fixture.Pair("""
            proc "T::Three()" () -> bv32 entry B0
            B0:
              %z: bv32 = const bv32 0
              %one: bv32 = const bv32 1
              %two: bv32 = const bv32 2
              goto B1
            B1:
              %i: bv32 = phi [B0: %z, B2: %i1]
              %c: bool = slt %i, %two
              br %c, B2, B3
            B2:
              %i1: bv32 = add %i, %one
              goto B1
            B3:
              ret %i
            ---
            proc "T::Three()" () -> bv32 entry B0
            B0:
              %two: bv32 = const bv32 2
              ret %two
            """);

        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(3, 10_000, []));

        Assert.Equal(new Equivalent(ProofMethod.Bounded, BoundedBy: 3), verdict with { Ladder = [] });
        Assert.Equal([RungOutcome.Proved], verdict.Ladder.Select(static s => s.Outcome));
    }

    [Fact]
    public void AWarmUpLoopNeedsTheThirdRung()
    {
        Verdict verdict = Verify(Fixture.Load("loops/warm-up"));

        Assert.Equal(
            [
                (ProofMethod.Bounded, RungOutcome.Inconclusive),
                (ProofMethod.LockstepInduction, RungOutcome.Inconclusive),
                (ProofMethod.KInduction, RungOutcome.Proved),
            ],
            verdict.Ladder.Select(static s => (s.Rung, s.Outcome)));
        Assert.Equal("the step obligation of loop 1 fails", verdict.Ladder[1].Detail);
    }

    [Fact]
    public void RungOneInlinesAHeapSelfCall()
    {
        Verdict verdict = Verify(Fixture.Load("loops/recursion-heap"));

        Assert.NotEqual(RungOutcome.NotApplicable, verdict.Ladder[0].Outcome);
    }

    [Fact]
    public void RungOneDoesNotInlineASelfCallWithASourceRefParameter()
    {
        const string Side = """
            proc "T::F(ref int)" (ref %n: bv32) entry B0
            B0:
              %z: bv32 = const bv32 0
              %c: bool = sle %n, %z
              br %c, B1, B2
            B1:
              ret outs(%n = %n)
            B2:
              call "T::F(ref int)"(%z) threw %t: bool
              ret outs(%n = %z)
            """;
        (IrProcedure old, IrProcedure @new) = Fixture.Pair(Side + "\n---\n" + Side);

        Verdict verdict = new Z3Backend().Verify(old, @new, new VerificationOptions(3, 60_000, []));

        Assert.Equal(
            (ProofMethod.Bounded, RungOutcome.NotApplicable, "self-recursion is not inlined: it has a by-ref parameter"),
            (verdict.Ladder[0].Rung, verdict.Ladder[0].Outcome, verdict.Ladder[0].Detail));
    }

    [Fact]
    public void TimeoutsOnEveryRungAreUnknownTimeout()
    {
        Fixture fixture = Fixture.Load("loops/loop-hard");
        VerificationOptions options = new(3, 50, []);

        Unknown unknown = Assert.IsType<Unknown>(new Z3Backend { Arithmetic = ArithmeticMode.Off }.Verify(fixture.Old, fixture.New, options));

        Assert.StartsWith("the step obligation of loop 1: solver returned unknown (", unknown.Detail, StringComparison.Ordinal);
        Assert.Equal(
            [RungOutcome.Timeout, RungOutcome.Timeout, RungOutcome.NotApplicable],
            unknown.Ladder.Select(static s => s.Outcome));

        // Ticket P1-031: by default rung 1's timeout is followed by its rounds on the abstracted product, which changes
        // neither the reason nor the detail, and the other rungs run as they did.
        Unknown refined = Assert.IsType<Unknown>(new Z3Backend().Verify(fixture.Old, fixture.New, options));
        Assert.Equal((unknown.Reason, unknown.Detail), (refined.Reason, refined.Detail));
        Assert.Equal(unknown.Ladder[0], refined.Ladder[0]);
        Assert.All(refined.Ladder.Skip(1).SkipLast(2), static s => Assert.Equal((ProofMethod.Bounded, true), (s.Rung, s.FactsAdded is not null)));
        Assert.Equal(unknown.Ladder.Skip(1).Select(static s => (s.Rung, s.Outcome)), refined.Ladder.TakeLast(2).Select(static s => (s.Rung, s.Outcome)));
    }

    /// <summary>
    /// Ticket P2-076 criterion 3. Neither limit ends a query here (a resource limit and a timeout far out of reach), so
    /// each hard query of <c>loop-hard</c> runs until it is interrupted, a second in. Each is then what a timeout is: the
    /// rung's step is <see cref="RungOutcome.Timeout"/>, the ladder goes on to the next rung, and the pair is
    /// Unknown(Timeout) with the same ladder <see cref="TimeoutsOnEveryRungAreUnknownTimeout"/> gets from the limits.
    /// </summary>
    [Fact]
    public void InterruptedQueryIsATimeoutAndTheLadderContinues()
    {
        Fixture fixture = Fixture.Load("loops/loop-hard");
        VerificationOptions unlimited = new(3, 3_600_000, []) { ResourceLimit = int.MaxValue };

        Verdict verdict = new LoopLadder(static () => new Context(), unlimited) { InterruptAfterMs = 1_000, Arithmetic = ArithmeticMode.Off }.Verify(fixture.Old, fixture.New);

        Unknown unknown = Assert.IsType<Unknown>(verdict);
        Assert.Equal(UnknownReason.Timeout, unknown.Reason);
        Assert.Equal("the step obligation of loop 1: solver returned unknown (interrupted)", unknown.Detail);
        Assert.Equal(
            [(ProofMethod.Bounded, RungOutcome.Timeout), (ProofMethod.LockstepInduction, RungOutcome.Timeout), (ProofMethod.KInduction, RungOutcome.NotApplicable)],
            unknown.Ladder.Select(static s => (s.Rung, s.Outcome)));
        Assert.Equal("solver returned unknown (interrupted)", unknown.Ladder[0].Detail);
    }

    [Fact]
    public void ABoundOnlyAHardConditionReachesTimesOutInRungOnesLastQuery()
    {
        // Identical sides and no opaque make rung 1's first two queries trivial; whether any input takes the back
        // edge (the hard, always-false condition) is the query that runs out of time.
        IrProcedure procedure = IrText.Parse("""
            proc "T::Rebuild(ulong, ulong)" (%a: bv64, %b: bv64) entry B0
            B0:
              goto B1
            B1:
              %q: bv64 = udiv %a, %b
              %m: bv64 = mul %q, %b
              %r: bv64 = urem %a, %b
              %s: bv64 = add %m, %r
              %broken: bool = ne %s, %a
              br %broken, B1, B2
            B2:
              ret
            """);

        Verdict verdict = new Z3Backend().Verify(procedure, procedure, new VerificationOptions(3, 50, []));

        Assert.Equal((ProofMethod.Bounded, RungOutcome.Timeout), (verdict.Ladder[0].Rung, verdict.Ladder[0].Outcome));
    }

    /// <summary>
    /// Ticket P2-076 criterion 2: what rung 1 hands to Z3 for every fixture of this project (the plain ones, one per
    /// instruction kind, and the loops) is what it handed before <see cref="Z3Backend.Inline"/> and the disposal of a
    /// query's context were made cheaper. The snapshot holds, per fixture, the SHA-256 of the assertions of the three
    /// solvers rung 1 makes (a divergence reaching no opaque, any opaque, any input past the bound), each rendered with
    /// <c>Expr.ToString()</c>; it was written at the commit the ticket branched from. A fixture rung 1 does not apply to
    /// says so. To see what changed in a digest that differs, print <c>Handed</c>'s text at both commits.
    /// </summary>
    [Fact]
    public Task AssertionsAreUnchanged()
    {
        IEnumerable<string> names =
        [
            .. FixtureTests.Names.Select(static row => row.Data),
            .. EncoderSnapshotTests.Kinds.Select(static row => "kinds/" + row.Data),
            .. Names.Select(static row => "loops/" + row.Data),
        ];
        StringBuilder digests = new();
        foreach (string name in names)
        {
            digests.Append(name).Append(' ').Append(Handed(Fixture.Load(name)) is { } text ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))) : "not-applicable").Append('\n');
        }

        return Verifier.Verify(digests.ToString());
    }

    /// <summary>
    /// The assertions of rung 1's three solvers on <paramref name="fixture"/>, one per line, or null when rung 1 does not
    /// apply to it (irreducible control flow, or a self-call it does not inline).
    /// </summary>
    private static string? Handed(Fixture fixture)
    {
        (IrProcedure old, IrProcedure @new, _) = ProductEncoder.ShareFragments(fixture.Old, fixture.New);
        if (!IrLoopAnalysis.Of(old).IsReducible || !IrLoopAnalysis.Of(@new).IsReducible || (IrUnroller.InliningObstacle(old) ?? IrUnroller.InliningObstacle(@new)) is not null)
        {
            return null;
        }

        VerificationOptions options = new(3, 60_000, []);
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, IrUnroller.Unroll(old, options.Bound), IrUnroller.Unroll(@new, options.Bound), options.CallIdentityMap);
        BoolExpr[] reachable = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        BoolExpr[][] queries =
        [
            [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable],
            [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable],
            [context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable)],
        ];
        StringBuilder text = new();
        foreach (BoolExpr[] query in queries)
        {
            using SolverQuery solver = Z3Backend.Query(context, encoding, options, query);
            foreach (BoolExpr assertion in solver.Solver.Assertions)
            {
                text.Append(assertion).Append('\n');
            }
        }

        return text.Replace("\r", string.Empty).ToString();
    }

    /// <summary>
    /// A fixture that expects a timeout (of any rung) gets 50 ms; every other one gets a minute per query. Rung 4 asks
    /// Spacer up to three times, about a second each on a developer machine, and the Windows gates leg runs these beside
    /// the property tests on four cores, where ten seconds once turned <c>fusion</c>'s proof into a timeout.
    /// </summary>
    private static Verdict Verify(Fixture fixture) =>
        new Z3Backend().Verify(fixture.Old, fixture.New, new VerificationOptions(3, fixture.Expected.StartsWith("Unknown(Timeout", StringComparison.Ordinal) || fixture.Expected.StartsWith("Unknown(NoInvariant", StringComparison.Ordinal) ? 50 : 60_000, []));

    private static string Describe(Verdict verdict) => verdict switch
    {
        Equivalent equivalent => $"Equivalent({Name(equivalent.Method)}{Mode(verdict)})",
        Divergent => $"Divergent({Name(verdict.Ladder[^1].Rung)}{Mode(verdict)})",
        Unknown unknown => $"Unknown({unknown.Reason}{Mode(verdict)})",
        _ => verdict.GetType().Name,
    };

    private static string Mode(Verdict verdict) => verdict.Ladder.LastOrDefault(static s => s.Mode is not null)?.Mode switch
    {
        ChcMode.Integers => ", int",
        ChcMode.BitVectors => ", bitvector",
        _ => "",
    };

    private static string Name(ProofMethod method) => method switch
    {
        ProofMethod.Bounded => "bounded",
        ProofMethod.LockstepInduction => "lockstep-induction",
        ProofMethod.KInduction => "k-induction",
        _ => "chc",
    };
}
