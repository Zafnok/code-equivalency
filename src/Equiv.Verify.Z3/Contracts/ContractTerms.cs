using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// A <see cref="CalleeContract"/> instantiated in a product (ADR 0036 decision 2; ticket P1-010): over a callee pair's two
/// outcomes, where it replaces "some observable differs", or over each aligned pair of calls in a caller's product, where
/// it replaces the shared call functions.
/// </summary>
internal static class ContractTerms
{
    /// <summary>
    /// Each conjunct of <paramref name="contract"/> over the callee pair's outcomes: each side's returned value, <c>threw</c>
    /// flag, exception type, call trace and final heap. A heap map neither side names is unchanged, one constant for both
    /// sides, and so is an unchanging input neither side has. A conjunct neither side has a term for is <c>true</c>.
    /// </summary>
    public static ImmutableArray<BoolExpr> Callee(
        Context context, SortMapper sorts, CalleeContract contract, FragmentEncoder old, FragmentEncoder @new, ImmutableArray<(SharedParameter Shared, Expr Term)> inputs)
    {
        return [.. contract.Conjuncts.Select(c => c.Term(context, sorts, Outcome(old, static s => s.Old), Outcome(@new, static s => s.New), Unchanging) ?? context.MkTrue())];

        ContractSide Outcome(FragmentEncoder side, Func<SharedParameter, IrParameter?> parameter) => new(
            leaf => leaf.Kind == PredicateLeafKind.Result ? Returned(side) : Heap(side, parameter, new HeapMap(leaf.Name, leaf.Type)),
            side.Threw,
            side.ExceptionType,
            side.Trace,
            map => Heap(side, parameter, map));

        Expr? Returned(FragmentEncoder side) =>
            side.Procedure.ReturnType is { } type ? side.ReturnValue(context.MkConst("ret.none", sorts.Sort(type))) : null;

        Expr Heap(FragmentEncoder side, Func<SharedParameter, IrParameter?> parameter, HeapMap map) =>
            Input(inputs, map.Name, map.Type) is { } input
                ? side.Final(parameter(input.Shared), input.Shared.Var, input.Term)
                : context.MkConst("contract.heap." + map.Name, sorts.Sort(map.Type));

        Expr Unchanging(string name, IrType type) => Input(inputs, name, type)?.Term ?? context.MkConst("contract.in." + name, sorts.Sort(type));
    }

    /// <summary>
    /// For every call to a callee under contract on the old side and every call to the same callee on the new side with the
    /// same argument types: when both are reached at the same position with equal arguments and an equal heap, their
    /// outcomes satisfy the contract. Calls that are not aligned so are left unrelated, as shared functions leave them.
    /// </summary>
    public static IEnumerable<BoolExpr> Caller(
        Context context,
        SortMapper sorts,
        IReadOnlyDictionary<string, CalleeContract> contracts,
        TraceEncoder calls,
        (FragmentEncoder Old, FragmentEncoder New) sides,
        ImmutableArray<(SharedParameter Shared, Expr Term)> inputs)
    {
        foreach (FragmentEncoder.CallSite old in sides.Old.CallSites)
        {
            string callee = calls.Canonical(Side.Old, old.Call.Callee);
            if (!contracts.TryGetValue(callee, out CalleeContract? contract))
            {
                continue;
            }

            foreach (FragmentEncoder.CallSite @new in sides.New.CallSites.Where(n => string.Equals(calls.Canonical(Side.New, n.Call.Callee), callee, StringComparison.Ordinal)))
            {
                if (!old.Args.Select(static a => a.Type).SequenceEqual(@new.Args.Select(static a => a.Type)))
                {
                    continue;
                }

                BoolExpr[] aligned =
                [
                    old.Reach,
                    @new.Reach,
                    context.MkEq(old.Position, @new.Position),
                    .. old.Args.Zip(@new.Args, (a, b) => context.MkEq(a.Term, b.Term)),
                    .. old.HeapIn.Zip(@new.HeapIn, context.MkEq),
                ];
                BoolExpr[] related = [context.MkTrue(), .. contract.Conjuncts.Select(c => c.Term(context, sorts, Call(old), Call(@new), Unchanging)).OfType<BoolExpr>()];
                yield return context.MkImplies(context.MkAnd(aligned), context.MkAnd(related));
            }
        }

        ContractSide Call(FragmentEncoder.CallSite site) => new(
            leaf => leaf.Kind == PredicateLeafKind.Result ? site.Result : Heap(site, new HeapMap(leaf.Name, leaf.Type)),
            site.Threw,
            ExceptionType: null,
            Calls: null,
            map => Heap(site, map));

        Expr? Heap(FragmentEncoder.CallSite site, HeapMap map) => calls.Heap.IndexOf(map) is var index and >= 0 ? site.HeapOut[index] : null;

        Expr? Unchanging(string name, IrType type) => Input(inputs, name, type)?.Term;
    }

    private static (SharedParameter Shared, Expr Term)? Input(ImmutableArray<(SharedParameter Shared, Expr Term)> inputs, string name, IrType type) =>
        inputs.Any(i => string.Equals(i.Shared.Var.Name, name, StringComparison.Ordinal) && i.Shared.Type == type)
            ? inputs.First(i => string.Equals(i.Shared.Var.Name, name, StringComparison.Ordinal) && i.Shared.Type == type)
            : null;
}
