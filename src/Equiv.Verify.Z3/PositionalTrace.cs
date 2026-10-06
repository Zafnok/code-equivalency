using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// "The two call traces are equal" written by position, with no sequence and no datatype (VERIFICATION-MODEL.md section
/// 5; ADR 0018; ticket P1-038). The traces are as long (<see cref="FragmentEncoder.Length"/>), and any old call and new
/// call that are both made and stand at the same position are the same event: the same canonical callee and the same
/// types of arguments and heap read, and equal terms (<see cref="TraceEncoder.Read"/>).
/// <para>
/// It is the relation <c>old.Trace = new.Trace</c> is, given that a path makes each call site at most once. Then a
/// reached site's position, the calls made before it, is the index of its event in its side's trace, and the reached
/// sites of a side have the indices 0 to length - 1, each once. Two sequences are equal exactly when they are as long and
/// equal at every index, and two events exactly when their callees are one and their values are as many, of the same
/// types and equal. Every product <see cref="ProductEncoder.Encode"/> builds is acyclic, so each site is made at most
/// once. A loop segment's cut events are events too, and the rungs that compare them still do so as sequences.
/// </para>
/// </summary>
internal static class PositionalTrace
{
    /// <summary>
    /// The most pairs of sites compared; a product with more keeps the sequence comparison. The form is quadratic in call
    /// sites, and Z3 decided no query of <c>gitextensions-8522</c> that compared more than 10,000 pairs
    /// (<c>docs/runs/2026-10-04-trace-encoding.md</c>).
    /// </summary>
    internal const int MaxPairs = 10_000;

    /// <summary>
    /// The traces of <paramref name="old"/> and <paramref name="new"/> are equal, or null when more than
    /// <paramref name="maxPairs"/> pairs of sites would be compared. A pair whose positions cannot meet on any path of the
    /// two control-flow graphs says nothing and is left out, and a pair whose positions are each one number is compared
    /// without them.
    /// </summary>
    public static BoolExpr? Equal(Context context, TraceEncoder calls, FragmentEncoder old, FragmentEncoder @new, int maxPairs = MaxPairs)
    {
        ImmutableArray<string> oldShapes = [.. old.CallSites.Select(s => Shape(calls, Side.Old, s))];
        ImmutableArray<string> newShapes = [.. @new.CallSites.Select(s => Shape(calls, Side.New, s))];
        List<BoolExpr> conjuncts = [context.MkEq(old.Length, @new.Length)];
        int pairs = 0;
        for (int i = 0; i < old.CallSites.Count; i++)
        {
            FragmentEncoder.CallSite a = old.CallSites[i];
            for (int j = 0; j < @new.CallSites.Count; j++)
            {
                FragmentEncoder.CallSite b = @new.CallSites[j];
                if (a.MaxPosition < b.MinPosition || b.MaxPosition < a.MinPosition)
                {
                    continue;
                }

                if (++pairs > maxPairs)
                {
                    return null;
                }

                if (SameEvent(context, a, b, string.Equals(oldShapes[i], newShapes[j], StringComparison.Ordinal)) is { } conjunct)
                {
                    conjuncts.Add(conjunct);
                }
            }
        }

        return context.MkAnd(conjuncts);
    }

    /// <summary>
    /// What one pair of sites whose positions can meet adds: where both are made at one position they are the same event,
    /// which two of different shapes never are. Null when their values are the same terms, which says nothing.
    /// </summary>
    private static BoolExpr? SameEvent(Context context, FragmentEncoder.CallSite a, FragmentEncoder.CallSite b, bool sameShape)
    {
        BoolExpr meet = a.MinPosition == a.MaxPosition && b.MinPosition == b.MaxPosition
            ? context.MkAnd(a.Reach, b.Reach)
            : context.MkAnd(a.Reach, b.Reach, context.MkEq(a.Position, b.Position));
        if (!sameShape)
        {
            return context.MkNot(meet);
        }

        BoolExpr[] equalities = [.. a.Event.Zip(b.Event).Where(static v => !v.First.Term.Equals(v.Second.Term)).Select(v => context.MkEq(v.First.Term, v.Second.Term))];
        return equalities.Length > 0 ? context.MkImplies(meet, context.MkAnd(equalities)) : null;
    }

    /// <summary>What two events must share to be equal whatever their values: the canonical callee and each value's type.</summary>
    private static string Shape(TraceEncoder calls, Side side, FragmentEncoder.CallSite site) =>
        string.Join('\u0001', site.Event.Select(static v => SortMapper.Name(v.Type)).Prepend(calls.Canonical(side, site.Call.Callee)));
}
