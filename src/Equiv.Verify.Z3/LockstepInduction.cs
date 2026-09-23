using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Rung = Equiv.Verify.Z3.LoopLadder.Rung;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 2, lockstep relational induction (VERIFICATION-MODEL.md section 5.1; ticket M3-002). Loops align when both
/// sides are reducible, have as many loops in the same nesting forest (paired in pre-order), and each paired
/// header's state (<see cref="IrFragmenter.State"/>) pairs up: a phi or live-in by <see cref="IrVar.SourceName"/>
/// when that is unambiguous, a parameter with the parameter it is shared with (ADR 0021), and the rest by position.
/// Both procedures are then cut at every header (<see cref="IrFragmenter.Segment"/>) and each cut-point pair is one
/// product query: the base, from equal inputs, and a step per header, from equal paired states. Each says the two
/// sides reach the same next header with equal states (its cut event), or leave the procedure with equal observables,
/// with equal traces on the way; all of them together prove the pair for every input, by induction on the number of
/// headers reached. A step's model is not a counterexample (its state may be unreachable); a base model is replayed
/// through the original procedures, and only a divergent replay refutes the pair. A self-call stays a call both
/// sides share, which is the mutual-summary treatment of recursion; its model is never replayed, since the call's
/// answer does not come from the procedure itself.
/// </summary>
internal sealed class LockstepInduction
{
    private readonly LoopLadder ladder;
    private readonly bool recursive;

    public LockstepInduction(LoopLadder ladder, IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape)
    {
        this.ladder = ladder;
        Old = old;
        New = @new;
        recursive = oldShape.IsSelfRecursive || newShape.IsSelfRecursive;
        Loops = [.. oldShape.Loops.Zip(newShape.Loops, static (o, n) => (o.Header, n.Header))];
        Misalignment = Align(oldShape, newShape);
        if (Misalignment is null)
        {
            Coupling[] couplings = [.. Loops.Select(l => Couple(old, @new, l.Old, l.New))];
            Misalignment = couplings
                .Select(static (c, i) => c.Failure is null ? null : $"loop {(i + 1).ToString(CultureInfo.InvariantCulture)}: {c.Failure}")
                .FirstOrDefault(static f => f is not null);
            OldCuts = [.. Loops.Zip(couplings, static (l, c) => (l.Old, c.Old))];
            NewCuts = [.. Loops.Zip(couplings, static (l, c) => (l.New, c.New))];
        }
    }

    public IrProcedure Old { get; }

    public IrProcedure New { get; }

    /// <summary>The paired loop headers, in pre-order of the nesting forest.</summary>
    public ImmutableArray<(IrBlockId Old, IrBlockId New)> Loops { get; }

    /// <summary>Why the loops do not align, or null when they do.</summary>
    public string? Misalignment { get; }

    /// <summary>Whether the rung ran and failed on a step obligation (so k-induction may still prove it).</summary>
    public bool StepFailed { get; private set; }

    /// <summary>The replay a base obligation's model gets: the original procedures, unless a side calls itself.</summary>
    public (IrProcedure Old, IrProcedure New)? Replay => recursive ? null : (Old, New);

    private ImmutableArray<(IrBlockId Header, ImmutableArray<IrVar> State)> OldCuts { get; } = [];

    private ImmutableArray<(IrBlockId Header, ImmutableArray<IrVar> State)> NewCuts { get; } = [];

    /// <summary>
    /// Pairs the state of <paramref name="oldHeader"/> with that of <paramref name="newHeader"/>: phis with phis, then
    /// live-ins with live-ins, each first by shared parameter (live-ins only), then by unambiguous source name, then
    /// in order. Both lists are returned in the paired order; a variable left without a partner, or two paired in
    /// order with different types, is a failure.
    /// </summary>
    public static Coupling Couple(IrProcedure old, IrProcedure @new, IrBlockId oldHeader, IrBlockId newHeader)
    {
        ImmutableArray<IrVar> oldState = IrFragmenter.State(old, oldHeader);
        ImmutableArray<IrVar> newState = IrFragmenter.State(@new, newHeader);
        int oldPhis = old.Blocks.First(b => b.Id == oldHeader).Instructions.OfType<IrPhi>().Count();
        int newPhis = @new.Blocks.First(b => b.Id == newHeader).Instructions.OfType<IrPhi>().Count();
        (IrVar Old, IrVar New)[] parameters =
        [
            .. ProductEncoder.Pair(old, @new)
                .Where(static s => s.Old is not null && s.New is not null)
                .Select(static s => (s.Old!.Var, s.New!.Var)),
        ];
        List<(IrVar Old, IrVar New)>? phis = Match(oldState[..oldPhis], newState[..newPhis], []);
        List<(IrVar Old, IrVar New)>? liveIns = Match(oldState[oldPhis..], newState[newPhis..], parameters);
        return phis is null || liveIns is null
            ? new Coupling([], [], [], $"the header states do not pair up ({Names(oldState)} against {Names(newState)})")
            : new Coupling(
                [.. phis.Concat(liveIns).Select(static p => p.Old)],
                [.. phis.Concat(liveIns).Select(static p => p.New)],
                [.. phis.Select(p => (oldState.IndexOf(p.Old), newState.IndexOf(p.New)))],
                Failure: null);
    }

    public Rung Prove()
    {
        if (Misalignment is not null)
        {
            return LoopLadder.NotApplicable(ProofMethod.LockstepInduction, $"the loops do not align: {Misalignment}", UnknownReason.UnalignedLoop);
        }

        LoopLadder.Obligation @base = ladder.Check(IrFragmenter.Segment(Old, start: null, OldCuts), IrFragmenter.Segment(New, start: null, NewCuts), Replay);
        if (@base.Status != Status.UNSATISFIABLE)
        {
            return @base.Counterexample is { } counterexample
                ? LoopLadder.Refuted(ProofMethod.LockstepInduction, "the base obligation has a counterexample that replays", counterexample)
                : LoopLadder.Failed(ProofMethod.LockstepInduction, @base, "the base obligation");
        }

        for (int i = 0; i < Loops.Length; i++)
        {
            LoopLadder.Obligation step = ladder.Check(IrFragmenter.Segment(Old, Loops[i].Old, OldCuts), IrFragmenter.Segment(New, Loops[i].New, NewCuts), replay: null);
            if (step.Status != Status.UNSATISFIABLE)
            {
                StepFailed = step.Status == Status.SATISFIABLE;
                return LoopLadder.Failed(ProofMethod.LockstepInduction, step, $"the step obligation of loop {(i + 1).ToString(CultureInfo.InvariantCulture)}");
            }
        }

        return LoopLadder.Proved(ProofMethod.LockstepInduction, $"base and {Loops.Length.ToString(CultureInfo.InvariantCulture)} step obligations hold", new Equivalent(ProofMethod.LockstepInduction));
    }

    /// <summary>Why the two loop forests differ, or null when they have the same shape.</summary>
    private static string? Align(IrLoopAnalysis old, IrLoopAnalysis @new)
    {
        static IEnumerable<int> Shape(IrLoopAnalysis analysis) =>
            analysis.Loops.Select(l => analysis.Loops.Select(static p => p.Header).ToList().IndexOf(l.Parent!));

        return (old, @new) switch
        {
            _ when !old.IsReducible || !@new.IsReducible => "a side's control flow is irreducible",
            _ when old.Loops.Length != @new.Loops.Length =>
                $"the old side has {old.Loops.Length.ToString(CultureInfo.InvariantCulture)} loops and the new side {@new.Loops.Length.ToString(CultureInfo.InvariantCulture)}",
            _ when !Shape(old).SequenceEqual(Shape(@new)) => "the loops nest differently",
            _ => null,
        };
    }

    /// <summary>Pairs two variable lists: <paramref name="known"/> pairs first, then unambiguous source names, then the rest in order.</summary>
    private static List<(IrVar Old, IrVar New)>? Match(ImmutableArray<IrVar> old, ImmutableArray<IrVar> @new, IReadOnlyList<(IrVar Old, IrVar New)> known)
    {
        List<(IrVar Old, IrVar New)> pairs = [.. known.Where(k => old.Contains(k.Old) && @new.Contains(k.New))];
        foreach (IrVar var in old.Where(v => v.SourceName is not null && pairs.TrueForAll(p => p.Old != v)))
        {
            IrVar[] named = [.. @new.Where(n => string.Equals(n.SourceName, var.SourceName, StringComparison.Ordinal) && pairs.TrueForAll(p => p.New != n))];
            bool unique = old.Where(o => string.Equals(o.SourceName, var.SourceName, StringComparison.Ordinal)).Take(2).Count() == 1;
            if (unique && named is [IrVar partner] && partner.Type == var.Type)
            {
                pairs.Add((var, partner));
            }
        }

        IrVar[] oldRest = [.. old.Where(v => pairs.TrueForAll(p => p.Old != v))];
        IrVar[] newRest = [.. @new.Where(v => pairs.TrueForAll(p => p.New != v))];
        if (oldRest.Length != newRest.Length || oldRest.Zip(newRest).Any(static p => p.First.Type != p.Second.Type))
        {
            return null;
        }

        pairs.AddRange(oldRest.Zip(newRest));
        return [.. pairs.OrderBy(p => old.IndexOf(p.Old))];
    }

    private static string Names(ImmutableArray<IrVar> state) => "[" + string.Join(", ", state.Select(static v => v.Name)) + "]";

    /// <summary>
    /// A header pair's coupled state: the old and new variables in paired order, the index pairs of the phis within
    /// each side's <see cref="IrFragmenter.State"/>, or why the states do not pair.
    /// </summary>
    public sealed record Coupling(ImmutableArray<IrVar> Old, ImmutableArray<IrVar> New, ImmutableArray<(int Old, int New)> Phis, string? Failure);
}
