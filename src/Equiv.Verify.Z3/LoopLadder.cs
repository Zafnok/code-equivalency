using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3;

/// <summary>
/// The loop ladder, rungs 1 to 3 (VERIFICATION-MODEL.md section 5.1; ADR 0008; ticket M3-002). Rung 1 always runs:
/// both sides unrolled <c>k</c> times (<see cref="VerificationOptions.Bound"/>) and self-calls inlined <c>k</c> deep
/// (<see cref="IrUnroller.Unroll"/>), then the M3-001 product query. The unrolled procedures run exactly as the
/// originals on the inputs they do not cut off, so a divergence is real (<see cref="Divergent"/>, replayed) and so
/// is a reachable opaque (<see cref="UnknownReason.Opaque"/>). No divergence proves the pair only when no input
/// reaches the bound, which is every acyclic pair (<see cref="ProofMethod.Bounded"/>, with
/// <see cref="Equivalent.BoundedBy"/> when a loop or self-call existed). Otherwise rung 2
/// (<see cref="LockstepInduction"/>) and rung 3 (<see cref="KInduction"/>) try for an unbounded proof, and a pair
/// none of them decides is Unknown: <see cref="UnknownReason.Opaque"/> when a failed obligation reached an opaque,
/// <see cref="UnknownReason.Recursion"/> when a side calls itself, <see cref="UnknownReason.UnalignedLoop"/> when the
/// loops do not align or an induction failed, and <see cref="UnknownReason.Timeout"/> when only the solver gave up.
/// Every verdict lists the rungs it ran in <see cref="Verdict.Ladder"/>.
/// </summary>
internal sealed class LoopLadder(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>Steps a replay of a looping procedure may take; a replay that runs out is not a counterexample.</summary>
    public const int ReplayBudget = 100_000;

    public VerificationOptions Options => options;

    public Verdict Verify(IrProcedure old, IrProcedure @new)
    {
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);
        bool recursive = oldShape.IsSelfRecursive || newShape.IsSelfRecursive;
        bool looping = recursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        List<Rung> rungs = [Bounded(old, @new, looping, oldShape.IsReducible && newShape.IsReducible)];
        if (looping && rungs[^1].Verdict is null)
        {
            LockstepInduction lockstep = new(this, old, @new, oldShape, newShape);
            rungs.Add(lockstep.Prove());
            if (rungs[^1].Verdict is null)
            {
                rungs.Add(new KInduction(this, lockstep).Prove());
            }
        }

        Verdict verdict = rungs[^1].Verdict ?? Undecided(rungs, recursive);
        return verdict with { Ladder = [.. rungs.Select(static r => r.Step)] };
    }

    /// <summary>
    /// Every rung on its own, whatever the others found (VERIFICATION-MODEL.md section 7: the soundness harness runs
    /// against every rung independently); k-induction runs whenever the loops align one to one.
    /// </summary>
    public IReadOnlyList<Rung> Independently(IrProcedure old, IrProcedure @new)
    {
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(@new);
        bool looping = oldShape.IsSelfRecursive || newShape.IsSelfRecursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        LockstepInduction lockstep = new(this, old, @new, oldShape, newShape);
        return [Bounded(old, @new, looping, oldShape.IsReducible && newShape.IsReducible), lockstep.Prove(), new KInduction(this, lockstep).Prove(force: true)];
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
            using Solver solver = Z3Backend.Query(context, encoding, options, [premise, context.MkOr(encoding.Differs, opaque, violation), .. Reachable(context, encoding)]);
            Status status = solver.Check();
            return status switch
            {
                Status.SATISFIABLE => new Obligation(
                    status,
                    replay is { } originals ? ModelDecoder.TryReplay(context, solver.Model, encoding, originals.Old, originals.New, ReplayBudget) : null,
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

    private T Session<T>(IrProcedure old, IrProcedure @new, Func<Context, ProductEncoding, T> body)
    {
        using Context context = createContext();
        return body(context, ProductEncoder.Encode(context, old, @new, options.CallIdentityMap));
    }

    /// <summary>
    /// Rung 1. Not applicable to irreducible control flow, or to a self-recursive side that cannot be inlined
    /// (<see cref="IrUnroller.InliningObstacle"/>).
    /// Three queries on the unrolled pair, each on inputs that reach no bound: a divergence reaching no opaque, then
    /// any opaque, then (for a looping pair) whether any input reaches the bound at all.
    /// </summary>
    private Rung Bounded(IrProcedure old, IrProcedure @new, bool looping, bool reducible)
    {
        int k = options.Bound;
        string bound = k.ToString(CultureInfo.InvariantCulture);
        if (!reducible)
        {
            return NotApplicable(ProofMethod.Bounded, "a side's control flow is irreducible", UnknownReason.UnalignedLoop);
        }

        if ((IrUnroller.InliningObstacle(old) ?? IrUnroller.InliningObstacle(@new)) is { } obstacle)
        {
            return NotApplicable(ProofMethod.Bounded, $"self-recursion is not inlined: {obstacle}", cause: null);
        }

        IrProcedure oldUnrolled = IrUnroller.Unroll(old, k);
        IrProcedure newUnrolled = IrUnroller.Unroll(@new, k);
        return Session(oldUnrolled, newUnrolled, (context, encoding) =>
        {
            BoolExpr[] reachable = Reachable(context, encoding);
            using Solver divergence = Z3Backend.Query(context, encoding, options, [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable]);
            Status diverges = divergence.Check();
            if (diverges != Status.UNSATISFIABLE)
            {
                return diverges == Status.SATISFIABLE
                    ? Found(ModelDecoder.Replay(context, divergence.Model, encoding, oldUnrolled, newUnrolled), bound)
                    : TimedOut(Z3Backend.Timeout(divergence, options));
            }

            using Solver opaque = Z3Backend.Query(context, encoding, options, [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable]);
            Status opaqueReached = opaque.Check();
            if (opaqueReached != Status.UNSATISFIABLE)
            {
                if (opaqueReached == Status.UNKNOWN)
                {
                    return TimedOut(Z3Backend.Timeout(opaque, options));
                }

                ImmutableArray<UnknownCause> causes = Z3Backend.ReachableOpaques(context, encoding, options, opaque.Model, reachable);
                string reasons = Z3Backend.OpaqueReasons(causes);
                return new Rung(
                    new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"an input reaches an opaque node: {reasons}"),
                    new Unknown(UnknownReason.Opaque, reasons) { Causes = causes });
            }

            return looping
                ? WithinBound(context, encoding, k, bound)
                : Proved(ProofMethod.Bounded, "no loop or self-call; every input checked", new Equivalent(ProofMethod.Bounded));
        });
    }

    /// <summary>Rung 1's last query: the unrolled pair agrees, so it is a proof exactly when no input reaches the bound.</summary>
    private Rung WithinBound(Context context, ProductEncoding encoding, int k, string bound)
    {
        using Solver cut = Z3Backend.Query(context, encoding, options, context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable));
        return cut.Check() switch
        {
            Status.UNSATISFIABLE => Proved(ProofMethod.Bounded, $"no input goes past the bound {bound}", new Equivalent(ProofMethod.Bounded, k)),
            Status.SATISFIABLE => new Rung(new LadderStep(ProofMethod.Bounded, RungOutcome.Inconclusive, $"no divergence within the bound {bound}, and some input goes past it")),
            _ => TimedOut(Z3Backend.Timeout(cut, options)),
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
