using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.Z3;

using HeapMap = Equiv.Verify.Z3.TraceEncoder.HeapMap;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// Finds a contract for one callee pair that a caller pair's use of it needs (ADR 0036 decision 2; ticket P1-010). The
/// candidate is every observed predicate (<see cref="ObservedPredicates"/>) made to agree across the sides, with the
/// callee's <c>threw</c> flag, exception type, calls and every heap map either the caller's product or the callee names.
/// A rejected candidate loses the conjuncts its model falsifies and is tried again, <see cref="MaxRounds"/> checks in all,
/// unless the model falsifies a conjunct the caller cannot see fail, which no weaker candidate can do without. A callee
/// pair that reaches an <see cref="IrOpaque"/>, takes a source parameter by reference, or returns different types on the
/// two sides gets no contract.
/// </summary>
internal sealed class ContractSearch(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>How many candidates are checked at most; the default is 4.</summary>
    public int MaxRounds { get; init; } = 4;

    /// <summary>The contract admitted for <paramref name="callee"/> as the caller pair uses it, or null.</summary>
    public CalleeContract? Admit(IrProcedure callerOld, IrProcedure callerNew, CalleePair callee)
    {
        if (!Eligible(callee))
        {
            return null;
        }

        CalleeContract contract = Candidate(callerOld, callerNew, callee);
        ContractVerifier verifier = new(createContext, options);
        for (int round = 0; round < MaxRounds; round++)
        {
            switch (verifier.Verify(callee.Old, callee.New, contract))
            {
                case ContractCheck.Admitted:
                    return contract;
                case ContractCheck.Rejected { Model: var model } when model.Falsified.All(i => contract.Conjuncts[i].Droppable):
                    contract = contract.Without(model.Falsified);
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The first candidate: every conjunct the caller's use of <paramref name="callee"/> suggests.</summary>
    public CalleeContract Candidate(IrProcedure callerOld, IrProcedure callerNew, CalleePair callee)
    {
        (IrProcedure shareOld, IrProcedure shareNew, _) = ProductEncoder.ShareFragments(callerOld, callerNew);
        ImmutableArray<HeapMap> callerHeap = ProductEncoder.HeapMaps(shareOld.Blocks.Concat(shareNew.Blocks).SelectMany(static b => b.Instructions.OfType<IrCall>()));
        IEnumerable<HeapMap> calleeHeap = callee.Old.Parameters.Concat(callee.New.Parameters)
            .Where(static p => p.Kind != IrParameterKind.In && p.Var.Type is IrMap && IrParameterNames.IsSynthesised(p.Var.Name))
            .Select(static p => new HeapMap(p.Var.Name, p.Var.Type))
            .Where(m => !callerHeap.Contains(m))
            .Distinct()
            .OrderBy(static m => m.Name, StringComparer.Ordinal);
        using Context context = createContext();
        IEnumerable<ObservedPredicate> predicates = ObservedPredicates.Of(callerOld, c => Is(c, options.CallIdentityMap.GetValueOrDefault(c.Callee.Value, c.Callee.Value)))
            .Concat(ObservedPredicates.Of(callerNew, c => Is(c, c.Callee.Value)))
            .DistinctBy(p => new CalleeContract([new ContractConjunct(ConjunctKind.Predicate, Predicate: p)]).Text(context), StringComparer.Ordinal);
        return new CalleeContract(
        [
            new ContractConjunct(ConjunctKind.Threw),
            new ContractConjunct(ConjunctKind.ExceptionType),
            new ContractConjunct(ConjunctKind.Calls),
            .. callerHeap.Select(static m => new ContractConjunct(ConjunctKind.Heap, m)),
            .. calleeHeap.Select(static m => new ContractConjunct(ConjunctKind.Heap, m, InCaller: false)),
            .. predicates.Select(static p => new ContractConjunct(ConjunctKind.Predicate, Predicate: p)),
        ]);

        bool Is(IrCall call, string identity) => string.Equals(identity, callee.Identity, StringComparison.Ordinal);
    }

    private static bool Eligible(CalleePair callee) =>
        callee.Old.ReturnType == callee.New.ReturnType
        && !new[] { callee.Old, callee.New }.Any(static body =>
            body.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Any()
            || body.Parameters.Any(static p => p.Kind != IrParameterKind.In && !IrParameterNames.IsSynthesised(p.Var.Name)));
}
