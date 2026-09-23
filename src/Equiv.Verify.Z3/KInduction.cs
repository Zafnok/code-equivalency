using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using Rung = Equiv.Verify.Z3.LoopLadder.Rung;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 3, k-induction (VERIFICATION-MODEL.md section 5.1; ticket M3-002): rung 2 with <c>k</c> earlier iterations
/// assumed equal, <c>k</c> being <see cref="Equiv.Core.VerificationOptions.Bound"/>. It runs when rung 2 failed on a
/// step obligation of the one loop each side has. The step clones the loop <c>k + 1</c> times in place
/// (<see cref="IrUnroller.UnrollInPlace"/>) and starts at the first copy's header from equal states; assuming both
/// sides reach the next <c>k</c> headers with equal paired phis, the segment must agree up to its cut at the first
/// header again. The base peels <c>k</c> iterations off the loop (<see cref="IrUnroller.Peel"/>) and checks, from equal
/// inputs, that the segment up to the remaining loop agrees and that both sides reach each peeled header with equal
/// paired phis, so the first <c>k + 1</c> arrivals at the header agree, as the step's window needs.
/// </summary>
internal sealed class KInduction(LoopLadder ladder, LockstepInduction lockstep)
{
    /// <summary>Runs the rung when rung 2 failed on a step obligation, or whenever the loops align one to one when <paramref name="force"/> is set.</summary>
    public Rung Prove(bool force = false)
    {
        bool applies = force || lockstep.StepFailed;
        if (!applies || lockstep.Misalignment is not null || lockstep.Loops.Length != 1)
        {
            string reason = (lockstep.Misalignment, applies) switch
            {
                ({ } misalignment, _) => $"the loops do not align: {misalignment}",
                (_, true) => "k-induction takes exactly one loop per side",
                _ => "lockstep induction did not fail on a step obligation",
            };
            return LoopLadder.NotApplicable(ProofMethod.KInduction, reason, lockstep.Misalignment is null ? null : UnknownReason.UnalignedLoop);
        }

        int k = ladder.Options.Bound;
        string name = $"{k.ToString(CultureInfo.InvariantCulture)}-induction";
        (IrBlockId oldHeader, IrBlockId newHeader) = lockstep.Loops[0];

        Window oldBase = Window.Of(IrUnroller.Peel(lockstep.Old, oldHeader, k + 1), last: true);
        Window newBase = Window.Of(IrUnroller.Peel(lockstep.New, newHeader, k + 1), last: true);
        LockstepInduction.Coupling baseCoupling = LockstepInduction.Couple(oldBase.Procedure, newBase.Procedure, oldBase.Cut, newBase.Cut);
        LoopLadder.Obligation @base = ladder.Check(
            oldBase.Segment(baseCoupling.Old, start: false),
            newBase.Segment(baseCoupling.New, start: false),
            lockstep.Replay,
            (context, encoding) => (context.MkTrue(), Disagree(context, Arrivals(context, encoding, oldBase, newBase, baseCoupling, ..k))));
        if (@base.Status != Status.UNSATISFIABLE)
        {
            return @base.Counterexample is { } counterexample
                ? LoopLadder.Refuted(ProofMethod.KInduction, $"the {name} base obligation has a counterexample that replays", counterexample)
                : LoopLadder.Failed(ProofMethod.KInduction, @base, $"the {name} base obligation");
        }

        Window oldStep = Window.Of(IrUnroller.UnrollInPlace(lockstep.Old, oldHeader, k + 1), last: false);
        Window newStep = Window.Of(IrUnroller.UnrollInPlace(lockstep.New, newHeader, k + 1), last: false);
        LockstepInduction.Coupling stepCoupling = LockstepInduction.Couple(oldStep.Procedure, newStep.Procedure, oldStep.Cut, newStep.Cut);
        LoopLadder.Obligation step = ladder.Check(
            oldStep.Segment(stepCoupling.Old, start: true),
            newStep.Segment(stepCoupling.New, start: true),
            replay: null,
            (context, encoding) => (Agree(context, Arrivals(context, encoding, oldStep, newStep, stepCoupling, 1..)), context.MkFalse()));
        return step.Status == Status.UNSATISFIABLE
            ? LoopLadder.Proved(ProofMethod.KInduction, $"the {name} base and step obligations hold", new Equivalent(ProofMethod.KInduction))
            : LoopLadder.Failed(ProofMethod.KInduction, step, $"the {name} step obligation");
    }

    /// <summary>
    /// For each header copy in <paramref name="copies"/>: both sides reach it, and whether its coupled phis are equal
    /// (a copy's phis are in its original header's order, so the coupling's phi index pairs apply to every copy).
    /// </summary>
    private static IEnumerable<(BoolExpr Reached, BoolExpr Equal)> Arrivals(Context context, ProductEncoding encoding, Window old, Window @new, LockstepInduction.Coupling coupling, Range copies)
    {
        (int offset, int length) = copies.GetOffsetAndLength(old.Headers.Length);
        return Enumerable.Range(offset, length).Select(j =>
        {
            BoolExpr[] equalities = [context.MkTrue(), .. coupling.Phis.Select(p => context.MkEq(encoding.Old.Vars[old.Phis(j)[p.Old].Name], encoding.New.Vars[@new.Phis(j)[p.New].Name]))];
            return (
                context.MkAnd(
                    encoding.Old.Reach.GetValueOrDefault(old.Headers[j], context.MkFalse()),
                    encoding.New.Reach.GetValueOrDefault(@new.Headers[j], context.MkFalse())),
                context.MkAnd(equalities));
        });
    }

    /// <summary>The base's violation: some peeled header copy both sides reach with unequal coupled phis.</summary>
    private static BoolExpr Disagree(Context context, IEnumerable<(BoolExpr Reached, BoolExpr Equal)> arrivals)
    {
        BoolExpr[] disagreements = [context.MkFalse(), .. arrivals.Select(a => context.MkAnd(a.Reached, context.MkNot(a.Equal)))];
        return context.MkOr(disagreements);
    }

    /// <summary>The step's premise: both sides reach every header copy after the first with equal coupled phis.</summary>
    private static BoolExpr Agree(Context context, IEnumerable<(BoolExpr Reached, BoolExpr Equal)> arrivals)
    {
        BoolExpr[] agreements = [context.MkTrue(), .. arrivals.Select(a => context.MkAnd(a.Reached, a.Equal))];
        return context.MkAnd(agreements);
    }

    /// <summary>A transformed procedure, its header copies in order, and the header its segment is cut at.</summary>
    private sealed record Window(IrProcedure Procedure, ImmutableArray<IrBlockId> Headers, IrBlockId Cut)
    {
        public static Window Of((IrProcedure Procedure, ImmutableArray<IrBlockId> Headers) copies, bool last) =>
            new(copies.Procedure, copies.Headers, last ? copies.Headers[^1] : copies.Headers[0]);

        /// <summary>The phis of header copy <paramref name="j"/>.</summary>
        public ImmutableArray<IrVar> Phis(int j) =>
            [.. Procedure.Blocks.First(b => b.Id == Headers[j]).Instructions.OfType<IrPhi>().Select(static p => p.Target)];

        /// <summary>The segment from the entry (<paramref name="start"/> false) or from <see cref="Cut"/>, cut at <see cref="Cut"/> with <paramref name="state"/>.</summary>
        public IrProcedure Segment(ImmutableArray<IrVar> state, bool start) =>
            IrFragmenter.Segment(Procedure, start ? Cut : null, [(Cut, state)]);
    }
}
