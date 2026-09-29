using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// One conjunct of a <see cref="CalleeContract"/>. <see cref="InCaller"/> says the caller's product names its heap map, so
/// that it sees the conjunct fail; only a conjunct the caller sees fail may be dropped from a rejected candidate
/// (<see cref="Droppable"/>), since a dropped conjunct no longer constrains anything.
/// </summary>
internal sealed record ContractConjunct(ConjunctKind Kind, HeapMap? Map = null, ObservedPredicate? Predicate = null, bool InCaller = true)
{
    public bool Droppable => Kind is ConjunctKind.Threw or ConjunctKind.Predicate || (Kind == ConjunctKind.Heap && InCaller);

    /// <summary>
    /// The conjunct over <paramref name="old"/> and <paramref name="new"/>, with each unchanging input's term from
    /// <paramref name="input"/>; null where a side has no term for what it relates (a caller never has exception types,
    /// its callee's calls or a heap map it does not name, and both sides of a product lack the same ones), which leaves it
    /// unconstrained.
    /// </summary>
    public BoolExpr? Term(Context context, SortMapper sorts, ContractSide old, ContractSide @new, Func<string, IrType, Expr?> input) => Kind switch
    {
        ConjunctKind.Threw => context.MkEq(old.Threw, @new.Threw),
        ConjunctKind.ExceptionType => old.ExceptionType is null ? null : context.MkImplies(context.MkAnd(old.Threw, @new.Threw), context.MkEq(old.ExceptionType, @new.ExceptionType!)),
        ConjunctKind.Calls => old.Calls is null ? null : context.MkEq(old.Calls, @new.Calls!),
        ConjunctKind.Heap => old.Heap(Map!) is { } left ? context.MkEq(left, @new.Heap(Map!)!) : null,
        _ => Agree(context, sorts, old, @new, input),
    };

    private BoolExpr? Agree(Context context, SortMapper sorts, ContractSide old, ContractSide @new, Func<string, IrType, Expr?> input)
    {
        if (Predicate!.Encode(context, sorts, Bind(old, input)) is not { } left)
        {
            return null;
        }

        Expr? right = Predicate.Encode(context, sorts, Bind(@new, input));
        return right is null ? null : context.MkOr(old.Threw, @new.Threw, context.MkEq(left, right));
    }

    private static Func<PredicateLeaf, Expr?> Bind(ContractSide side, Func<string, IrType, Expr?> input) =>
        leaf => leaf.Kind == PredicateLeafKind.Input ? input(leaf.Name, leaf.Type) : side.Leaf(leaf);
}
