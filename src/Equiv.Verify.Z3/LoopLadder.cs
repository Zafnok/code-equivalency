using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Equiv.Verify.Z3.Contracts;
using Equiv.Verify.Z3.Ladder;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3;

/// <summary>
/// The loop ladder, rungs 1 to 4 (VERIFICATION-MODEL.md section 5.1; ADR 0008; tickets M3-002 and P1-001). Rung 1 always
/// runs: both sides unrolled <c>k</c> times (<see cref="VerificationOptions.Bound"/>) and self-calls inlined <c>k</c> deep
/// (<see cref="IrUnroller.Unroll"/>), then the M3-001 product query. The unrolled procedures run exactly as the
/// originals on the inputs they do not cut off, so a divergence is real (<see cref="Divergent"/>, replayed) and so
/// is a reachable opaque (<see cref="UnknownReason.Opaque"/>). No divergence proves the pair only when no input
/// reaches the bound, which is every acyclic pair (<see cref="ProofMethod.Bounded"/>, with
/// <see cref="Equivalent.BoundedBy"/> when a loop or self-call existed). Otherwise rung 2
/// (<see cref="LockstepInduction"/>) and rung 3 (<see cref="KInduction"/>) try for an unbounded proof. A pair none of
/// them decides is Unknown: <see cref="UnknownReason.Opaque"/> when a failed obligation reached an opaque,
/// <see cref="UnknownReason.Recursion"/> when a side calls itself, <see cref="UnknownReason.UnalignedLoop"/> when the
/// loops do not align or an induction failed, and <see cref="UnknownReason.Timeout"/> when only the solver gave up;
/// except that a pair that would be UnalignedLoop goes to rung 4 (<see cref="SpacerRung"/>) first, which decides it
/// whenever neither side calls. When rung 4 times out, rung 5 (<see cref="LlmInvariantRung"/>) asks <see cref="Traces"/>
/// for the invariant (ticket P1-009), and then, unless that proved the pair, <paramref name="proposer"/> when it is given
/// (<c>--invariant-model</c>), one ladder step per round. Every verdict lists the rungs it ran in <see cref="Verdict.Ladder"/>.
/// </summary>
internal sealed class LoopLadder(Func<Context> createContext, VerificationOptions options, IInvariantProposer? proposer = null)
{
    /// <summary>Steps a replay of a looping procedure may take; a replay that runs out is not a counterexample.</summary>
    public const int ReplayBudget = 100_000;

    public VerificationOptions Options => options;

    /// <summary>
    /// The timeout of rung 5's obligation checks, <see cref="VerificationOptions.TimeoutMs"/> unless set; a test sets it to
    /// force rung 4 to time out while rung 5 still decides. The resource limit is the pair's on every rung.
    /// </summary>
    public int? InvariantTimeoutMs { get; init; }

    /// <summary>
    /// How long a query of rungs 1 to 3 may run before it is interrupted, <see cref="Z3Backend.InterruptAfterMs"/> unless
    /// set; a test sets it below the timeout, to interrupt a query no limit has ended yet (ticket P2-076 criterion 3).
    /// </summary>
    public long? InterruptAfterMs { get; init; }

    /// <summary>
    /// The local proposer rung 5 asks first, on by default because it runs in process and sends nothing (ticket P1-009);
    /// a test sets it to null to run the model's proposer alone, and so does a pass of <c>equiv compare</c> that turns
    /// <see cref="VerificationOptions.LocalProposer"/> off (ADR 0049; ticket P1-032).
    /// </summary>
    public IInvariantProposer? Traces { get; init; } = options.LocalProposer ? new TraceInvariantProposer() : null;

    /// <summary>
    /// The callee contracts a caller's product relates its calls by (ticket P1-010; <see cref="ProductEncoder.Encode"/>), or
    /// null to share every call function.
    /// </summary>
    public CallerContracts? Contracts { get; init; }

    /// <summary>
    /// The contract a callee pair's product checks in place of equal observables (ticket P1-010; <see cref="ContractVerifier"/>),
    /// or null.
    /// </summary>
    public CalleeContract? Relation { get; init; }

    /// <summary>
    /// Runs the ladder on the pair with its shared fragments encoded as calls (<see cref="ProductEncoder.ShareFragments"/>).
    /// An Unknown that depends on one points at that fragment's line on each side (ADR 0027 decision 4).
    /// </summary>
    public Verdict Verify(IrProcedure old, IrProcedure @new)
    {
        ProductEncoder.SharedFragments shared = Stages.Timed(options, Stages.Share, () => ProductEncoder.ShareFragments(old, @new));
        Verdict verdict = Climb(shared.Old, shared.New);
        if (verdict is not Unknown { Reason: UnknownReason.Abstraction } unknown)
        {
            return verdict;
        }

        ImmutableArray<Abstraction> located = [.. unknown.Abstractions.Select(shared.Locate)];
        return unknown with { Abstractions = located, Causes = [.. located.Select(static a => a.Cause).OfType<UnknownCause>()] };
    }

    private Verdict Climb(IrProcedure old, IrProcedure @new)
    {
        (IrLoopAnalysis oldShape, IrLoopAnalysis newShape) = Stages.Timed(options, Stages.Shape, () => (IrLoopAnalysis.Of(old), IrLoopAnalysis.Of(@new)));
        bool recursive = oldShape.IsSelfRecursive || newShape.IsSelfRecursive;
        bool looping = recursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        List<Rung> rungs = [Timed(() => Bounded(old, @new, looping, oldShape.IsReducible && newShape.IsReducible))];
        if (looping && rungs[^1].Verdict is null)
        {
            LockstepInduction lockstep = Stages.Timed(options, Stages.Couple, () => new LockstepInduction(this, old, @new, oldShape, newShape));
            rungs.Add(Timed(lockstep.Prove));
            if (rungs[^1].Verdict is null)
            {
                rungs.Add(Timed(() => new KInduction(this, lockstep).Prove()));
            }

            if (rungs[^1].Verdict is null && UndecidedReason(rungs, recursive) == UnknownReason.UnalignedLoop)
            {
                rungs.Add(Timed(() => new SpacerRung(createContext, options).Prove(old, @new)));
            }

            ProposeInvariants(rungs, old, @new);
        }

        Verdict verdict = rungs[^1].Verdict ?? Undecided(rungs, recursive);
        return verdict with { Ladder = [.. rungs.Select(static r => r.Step)] };
    }

    /// <summary>Runs one rung and, at <c>debug</c>, writes its <c>rung=… took=… result=…</c> line (ticket M4-014).</summary>
    private Rung Timed(Func<Rung> rung)
    {
        long started = TimeProvider.System.GetTimestamp();
        Rung result = rung();
        LogRung(result.Step, started);
        return result;
    }

    /// <summary>One line per rung; the text is built only when the log is at <c>debug</c>. Rung 5's rounds are one rung, named by its last step.</summary>
    /// <summary>
    /// Rung 5 after rung 4 timed out: <see cref="Traces"/> first, then the model's proposer unless that proved the pair.
    /// </summary>
    private void ProposeInvariants(List<Rung> rungs, IrProcedure old, IrProcedure @new)
    {
        VerificationOptions invariantOptions = options with { TimeoutMs = InvariantTimeoutMs ?? options.TimeoutMs };
        if (rungs[^1].Verdict is Unknown { Reason: UnknownReason.ChcTimeout } && Traces is not null)
        {
            rungs.AddRange(Invariant(old, @new, invariantOptions, Traces, TraceInvariantProposer.Name, ProofMethod.TraceInvariant));
        }

        if (rungs[^1].Verdict is Unknown { Reason: UnknownReason.ChcTimeout or UnknownReason.NoInvariant } && proposer is not null)
        {
            rungs.AddRange(Invariant(old, @new, invariantOptions, proposer, options.InvariantModel!, ProofMethod.LlmInvariant));
        }
    }

    /// <summary>Rung 5 with one proposer, logged at debug as one rung with its last round's outcome.</summary>
    private ImmutableArray<Rung> Invariant(IrProcedure old, IrProcedure @new, VerificationOptions invariantOptions, IInvariantProposer asked, string proposedBy, ProofMethod method)
    {
        long started = TimeProvider.System.GetTimestamp();
        ImmutableArray<Rung> rounds = new LlmInvariantRung(createContext, invariantOptions, asked, proposedBy, method).Prove(old, @new);
        LogRung(rounds[^1].Step, started);
        return rounds;
    }

    private void LogRung(LadderStep step, long started)
    {
        if (!options.Log.IsDebug)
        {
            return;
        }

        double took = TimeProvider.System.GetElapsedTime(started).TotalSeconds;
        options.Log.Detail(string.Create(CultureInfo.InvariantCulture, $"rung={RungName(step.Rung)} took={took:0.###}s result={ResultName(step.Outcome)}"));
    }

    private static string RungName(ProofMethod method) => method switch
    {
        ProofMethod.Bounded => "bounded",
        ProofMethod.LockstepInduction => "lockstep-induction",
        ProofMethod.KInduction => "k-induction",
        ProofMethod.Chc => "chc",
        ProofMethod.TraceInvariant => "trace-invariant",
        _ => "llm-invariant",
    };

    private static string ResultName(RungOutcome outcome) => outcome switch
    {
        RungOutcome.Proved => "unsat",
        RungOutcome.Refuted => "sat",
        RungOutcome.Timeout => "timeout",
        RungOutcome.NotApplicable => "not-applicable",
        _ => "unknown",
    };

    /// <summary>
    /// Every rung on its own, whatever the others found (VERIFICATION-MODEL.md section 7: the soundness harness runs
    /// against every rung independently); k-induction runs whenever the loops align one to one, and rung 4 whenever
    /// neither side calls. Each rung runs when it is enumerated, so a caller can stop after the rungs it checks.
    /// </summary>
    public IEnumerable<Rung> Independently(IrProcedure old, IrProcedure @new)
    {
        (old, @new, _) = ProductEncoder.ShareFragments(old, @new);
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);
        bool looping = oldShape.IsSelfRecursive || newShape.IsSelfRecursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        LockstepInduction lockstep = new(this, old, @new, oldShape, newShape);
        yield return Bounded(old, @new, looping, oldShape.IsReducible && newShape.IsReducible);
        yield return lockstep.Prove();
        yield return new KInduction(this, lockstep).Prove(force: true);
        yield return new SpacerRung(createContext, options).Prove(old, @new);
    }

    /// <summary>
    /// Checks one obligation of an induction rung: whenever <c>premise</c> holds, the two fragments agree on every
    /// observable, reach no opaque, and make <c>violation</c> false. A satisfying model is replayed through
    /// <paramref name="replay"/>, the original procedures, when given; only a replay that diverges is a counterexample.
    /// </summary>
    public Obligation Check(
        IrProcedure oldFragment,
        IrProcedure newFragment,
        (IrProcedure Old, IrProcedure New)? replay,
        Func<Context, ProductEncoding, (BoolExpr Premise, BoolExpr Violation)>? terms = null) =>
        Session(oldFragment, newFragment, (context, encoding) =>
        {
            (BoolExpr premise, BoolExpr violation) = terms?.Invoke(context, encoding) ?? (context.MkTrue(), context.MkFalse());
            BoolExpr opaque = context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew);
            using SolverQuery solver = Z3Backend.Query(context, encoding, options, [premise, context.MkOr(encoding.Differs, opaque, violation), .. Reachable(context, encoding)]);
            Status status = solver.Check(options, "obligation", InterruptAfterMs);
            return status switch
            {
                Status.SATISFIABLE => new Obligation(
                    status,
                    replay is { } originals ? Stages.Timed(options, Stages.Replay, () => ModelDecoder.TryReplay(context, solver.Model, encoding, originals.Old, originals.New, ReplayBudget)) : null,
                    solver.Model.Eval(opaque, completion: true).IsTrue)
                {
                    Causes = Z3Backend.Causes(encoding, Z3Backend.Reached(solver.Model, encoding)),
                },
                Status.UNSATISFIABLE => new Obligation(status),
                _ => new Obligation(status, Detail: Z3Backend.Timeout(solver, options)),
            };
        });

    /// <summary>The result of an obligation the rung failed: an inconclusive or timed-out step and the cause of the eventual Unknown.</summary>
    public static Rung Failed(ProofMethod rung, Obligation obligation, string what) => obligation.Status switch
    {
        Status.SATISFIABLE when obligation.Opaque =>
            new Rung(new LadderStep(rung, RungOutcome.Inconclusive, $"{what} reaches an opaque node"), Cause: UnknownReason.Opaque) { Causes = obligation.Causes },
        Status.SATISFIABLE => new Rung(new LadderStep(rung, RungOutcome.Inconclusive, $"{what} fails"), Cause: UnknownReason.UnalignedLoop),
        _ => new Rung(new LadderStep(rung, RungOutcome.Timeout, $"{what}: {obligation.Detail}"), Cause: UnknownReason.Timeout),
    };

    /// <summary>A step that proved the pair.</summary>
    public static Rung Proved(ProofMethod rung, string detail, Verdict verdict) => new(new LadderStep(rung, RungOutcome.Proved, detail), verdict);

    /// <summary>A step that found a real counterexample.</summary>
    public static Rung Refuted(ProofMethod rung, string detail, Counterexample counterexample) =>
        new(new LadderStep(rung, RungOutcome.Refuted, detail), new Divergent(counterexample));

    /// <summary>A step that did not apply to the pair; <paramref name="cause"/> is why the pair stays undecided.</summary>
    public static Rung NotApplicable(ProofMethod rung, string detail, UnknownReason? cause) => new(new LadderStep(rung, RungOutcome.NotApplicable, detail), Cause: cause);

    private static BoolExpr[] Reachable(Context context, ProductEncoding encoding) =>
        [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];

    private static Unknown Undecided(List<Rung> rungs, bool recursive)
    {
        UnknownReason reason = UndecidedReason(rungs, recursive);
        Rung cause = rungs.LastOrDefault(r => r.Cause == reason) ?? rungs[^1];
        return new Unknown(reason, cause.Step.Detail) { Causes = cause.Causes };
    }

    /// <summary>The first that applies: an opaque node reached, a self-call, loops no rung aligned or proved, else a timeout.</summary>
    private static UnknownReason UndecidedReason(List<Rung> rungs, bool recursive) =>
        (rungs.Exists(static r => r.Cause == UnknownReason.Opaque), recursive, rungs.Exists(static r => r.Cause == UnknownReason.UnalignedLoop)) switch
        {
            (true, _, _) => UnknownReason.Opaque,
            (_, true, _) => UnknownReason.Recursion,
            (_, _, true) => UnknownReason.UnalignedLoop,
            _ => UnknownReason.Timeout,
        };

    private T Session<T>(IrProcedure old, IrProcedure @new, Func<Context, ProductEncoding, T> body, ProductEncoder.TraceComparison traces = ProductEncoder.TraceComparison.Sequence) =>
        Stages.WithContext(
            options,
            createContext,
            context => body(context, Stages.Timed(options, Stages.Encode, () => ProductEncoder.Encode(context, old, @new, options.CallIdentityMap, Contracts, Relation, traces))));

    /// <summary>
    /// Rung 1. Not applicable to irreducible control flow, or to a self-recursive side that cannot be inlined
    /// (<see cref="IrUnroller.InliningObstacle"/>).
    /// Three queries on the unrolled pair, each on inputs that reach no bound: a divergence reaching no opaque, then
    /// any opaque, then (for a looping pair) whether any input reaches the bound at all. Each is asked of Z3 and, when Z3
    /// gives up and a second solver is configured, of that solver (<see cref="SecondSolver"/>; ADR 0050); the step names
    /// the solver when it answered one. The product compares the call traces by position (<see cref="PositionalTrace"/>;
    /// ticket P1-038); every other rung's compares them as sequences.
    /// </summary>
    private Rung Bounded(IrProcedure old, IrProcedure @new, bool looping, bool reducible)
    {
        int k = options.Bound;
        if (!reducible)
        {
            return NotApplicable(ProofMethod.Bounded, "a side's control flow is irreducible", UnknownReason.UnalignedLoop);
        }

        if ((IrUnroller.InliningObstacle(old) ?? IrUnroller.InliningObstacle(@new)) is { } obstacle)
        {
            return NotApplicable(ProofMethod.Bounded, $"self-recursion is not inlined: {obstacle}", cause: null);
        }

        if (Stages.Timed(options, Stages.Unroll, () => Unrolled(old, @new, k)) is not var (oldUnrolled, newUnrolled))
        {
            // No product to ask about: the loops are left to the rungs that do not unroll them k times.
            return NotApplicable(ProofMethod.Bounded, TooLargeToUnroll(k), UnknownReason.UnalignedLoop);
        }

        return Session(
            oldUnrolled,
            newUnrolled,
            (context, encoding) =>
            {
                SecondSolver solvers = new(context, encoding, options, InterruptAfterMs);
                return solvers.Tagged(Bounded(context, encoding, solvers, (oldUnrolled, newUnrolled), looping));
            },
            ProductEncoder.TraceComparison.Positional);
    }

    /// <summary>Rung 1's queries on the encoding of the <paramref name="unrolled"/> pair, in order.</summary>
    private Rung Bounded(Context context, ProductEncoding encoding, SecondSolver solvers, (IrProcedure Old, IrProcedure New) unrolled, bool looping)
    {
        string bound = options.Bound.ToString(CultureInfo.InvariantCulture);
        BoolExpr[] reachable = Reachable(context, encoding);
        using SecondSolver.Asked divergence = solvers.Check("divergence", [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable]);
        if (divergence.Status != Status.UNSATISFIABLE)
        {
            return divergence.Status == Status.SATISFIABLE
                ? Found(Stages.Timed(options, Stages.Replay, () => ModelDecoder.Replay(context, divergence.Model, encoding, unrolled.Old, unrolled.New)), bound)
                : TimedOut(divergence.Timeout());
        }

        using SecondSolver.Asked opaque = solvers.Check("opaque", [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable]);
        if (opaque.Status != Status.UNSATISFIABLE)
        {
            if (opaque.Status == Status.UNKNOWN)
            {
                return TimedOut(opaque.Timeout());
            }

            ImmutableArray<UnknownCause> causes = Z3Backend.ReachableOpaques(context, encoding, options, opaque.Model, reachable, InterruptAfterMs);
            string reasons = Z3Backend.OpaqueReasons(causes);
            return new Rung(
                new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"an input reaches an opaque node: {reasons}"),
                new Unknown(UnknownReason.Opaque, reasons) { Causes = causes, Scope = OpaqueScope(encoding, looping) });
        }

        return looping
            ? WithinBound(solvers, context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable), bound)
            : Proved(ProofMethod.Bounded, "no loop or self-call; every input checked", new Equivalent(ProofMethod.Bounded));
    }

    /// <summary>
    /// Both sides unrolled <paramref name="bound"/> times, or null when either would hold more than
    /// <see cref="IrUnroller.MaxBlocks"/> blocks (ticket P1-032): rung 1, the contract check and the failure-refinement
    /// queries then have no product to ask about, and say so instead of unrolling without end.
    /// </summary>
    internal static (IrProcedure Old, IrProcedure New)? Unrolled(IrProcedure old, IrProcedure @new, int bound) =>
        IrUnroller.UnrollWithin(old, bound) is { } oldUnrolled && IrUnroller.UnrollWithin(@new, bound) is { } newUnrolled ? (oldUnrolled, newUnrolled) : null;

    /// <summary>Why a pair <see cref="Unrolled"/> refused has no rung 1.</summary>
    internal static string TooLargeToUnroll(int bound) =>
        string.Create(CultureInfo.InvariantCulture, $"a side unrolled {bound} times holds more than {IrUnroller.MaxBlocks} blocks");

    /// <summary>
    /// The scope of rung 1's opaque Unknown, reached only once the first query of ADR 0014 was unsatisfiable (ADR 0029
    /// decision 4). That query proved the pair on every input reaching no opaque node, which is the residual claim, but
    /// only when the pair is acyclic: on a looping pair it ran on the unrolled procedures, so it says nothing past the
    /// bound. A whole-body opaque is on every path, so it is always among the causes and the Unknown is the method's.
    /// </summary>
    private static UnknownScope OpaqueScope(ProductEncoding encoding, bool looping) =>
        looping || encoding.Opaques.Any(static o => o.Node.WholeBody) ? UnknownScope.Method : UnknownScope.Line;

    /// <summary>Rung 1's last query: the unrolled pair agrees, so it is a proof exactly when no input reaches the bound.</summary>
    private Rung WithinBound(SecondSolver solvers, BoolExpr pastTheBound, string bound)
    {
        using SecondSolver.Asked cut = solvers.Check("bound", pastTheBound);
        return cut.Status switch
        {
            Status.UNSATISFIABLE => Proved(ProofMethod.Bounded, $"no input goes past the bound {bound}", new Equivalent(ProofMethod.Bounded, options.Bound)),
            Status.SATISFIABLE => new Rung(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"no divergence within the bound {bound}, and some input goes past it")),
            _ => TimedOut(cut.Timeout()),
        };
    }

    /// <summary>Rung 1's replayed divergence: a real one refutes the pair, one that depends on an abstraction ends the ladder Unknown (ADR 0026).</summary>
    private static Rung Found(Verdict replayed, string bound) => replayed switch
    {
        Divergent divergent => Refuted(ProofMethod.Bounded, $"a divergence within {bound} iterations", divergent.Counterexample),
        _ => new Rung(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"a divergence within {bound} iterations depends on an abstraction"), replayed),
    };

    private static Rung TimedOut(string detail) =>
        new(new LadderStep(ProofMethod.Bounded, RungOutcome.Timeout, detail), Cause: UnknownReason.Timeout);

    /// <summary>
    /// What one rung did: its ladder step, the verdict when it decided the pair, and otherwise why it did not, with the
    /// opaque nodes that made it fail as <see cref="Causes"/>.
    /// </summary>
    public sealed record Rung(LadderStep Step, Verdict? Verdict = null, UnknownReason? Cause = null)
    {
        public ImmutableArray<UnknownCause> Causes { get; init; } = [];
    }

    /// <summary>
    /// An obligation's solver status, and for a model, its replayed counterexample (if real), whether it reaches an
    /// opaque, and the opaque nodes it reaches as <see cref="Causes"/>.
    /// </summary>
    public sealed record Obligation(Status Status, Counterexample? Counterexample = null, bool Opaque = false, string Detail = "")
    {
        public ImmutableArray<UnknownCause> Causes { get; init; } = [];
    }
}
