using System.Collections.Immutable;
using System.Text.RegularExpressions;

using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// A relational contract K over a matched callee pair (ADR 0036 decision 2; ticket P1-010): a conjunction over each side's
/// outcome and heap effect for one call, both sides called with equal arguments on an equal heap. A caller's product
/// relates each aligned pair of calls by K instead of by a shared function (<see cref="CallerContracts"/>), and a callee
/// pair's product asks whether K can fail (<see cref="ContractVerifier"/>).
/// </summary>
internal sealed partial record CalleeContract(ImmutableArray<ContractConjunct> Conjuncts)
{
    /// <summary>K without the conjuncts at <paramref name="indices"/>.</summary>
    public CalleeContract Without(IEnumerable<int> indices)
    {
        HashSet<int> dropped = [.. indices];
        return new CalleeContract([.. Conjuncts.Where((_, i) => !dropped.Contains(i))]);
    }

    /// <summary>
    /// K in SMT-LIB over named sides: <c>r.old</c> and <c>r.new</c> are the results, <c>threw.*</c> the <c>threw</c> flags,
    /// <c>type.*</c> the exception types, <c>calls.*</c> the call traces, <c>heap.&lt;map&gt;.*</c> the heap maps each side
    /// leaves, and an unchanging input its own name, on one line.
    /// </summary>
    public string Text(Context context)
    {
        SortMapper sorts = new(context);
        Sort calls = context.MkUninterpretedSort("Calls");
        BoolExpr[] terms = [.. Conjuncts.Select(c => c.Term(context, sorts, Named("old"), Named("new"), (name, type) => context.MkConst(name, sorts.Sort(type)))!)];
        return Whitespace.Replace(context.MkAnd(terms).ToString(), " ");

        ContractSide Named(string side) => new(
            leaf => leaf.Kind switch
            {
                PredicateLeafKind.Result => context.MkConst("r." + side, sorts.Sort(leaf.Type)),
                PredicateLeafKind.Heap => Heap(new HeapMap(leaf.Name, leaf.Type), side),
                _ => context.MkConst(leaf.Name, sorts.Sort(leaf.Type)),
            },
            context.MkBoolConst("threw." + side),
            context.MkIntConst("type." + side),
            context.MkConst("calls." + side, calls),
            map => Heap(map, side));

        Expr Heap(HeapMap map, string side) => context.MkConst($"heap.{map.Name}.{side}", sorts.Sort(map.Type));
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace { get; }
}
