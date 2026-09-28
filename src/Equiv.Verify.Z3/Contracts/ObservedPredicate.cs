using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// A Bool a caller computes from one call's outputs (ADR 0036 decision 2; ticket P1-010): a branch condition, a comparison
/// or a <c>== null</c> test over the call's result or a heap map the call left, through constants and the caller's
/// unchanging synthesised inputs only. <see cref="Slice"/> is the instructions that compute <see cref="Output"/>, in
/// dependency order, and <see cref="Leaves"/> binds every variable they read that none of them defines. A callee contract
/// asks that both sides' calls give the predicate the same value.
/// </summary>
internal sealed record ObservedPredicate(ImmutableArray<IrInstruction> Slice, IrVar Output, ImmutableDictionary<string, PredicateLeaf> Leaves)
{
    /// <summary>The predicate's term with each leaf bound by <paramref name="bind"/>, or null when a leaf has no term there.</summary>
    public Expr? Encode(Context context, SortMapper sorts, Func<PredicateLeaf, Expr?> bind)
    {
        Dictionary<string, Expr> terms = new(StringComparer.Ordinal);
        foreach ((string name, PredicateLeaf leaf) in Leaves)
        {
            if (bind(leaf) is not { } term || !term.Sort.Equals(sorts.Sort(leaf.Type)))
            {
                return null;
            }

            terms[name] = term;
        }

        foreach (IrInstruction instruction in Slice)
        {
            (IrVar target, Expr value) = instruction switch
            {
                IrConst constant => (constant.Target, sorts.Literal(constant.Value)),
                IrBinary binary => (binary.Target, FragmentEncoder.Binary(context, binary.Op, terms[binary.A.Name], terms[binary.B.Name])),
                IrUnary unary => (unary.Target, FragmentEncoder.Unary(context, unary, terms[unary.A.Name])),
                _ => Read(context, (IrMapRead)instruction, terms),
            };
            terms[target.Name] = value;
        }

        return terms[Output.Name];
    }

    private static (IrVar, Expr) Read(Context context, IrMapRead read, Dictionary<string, Expr> terms) =>
        (read.Target, context.MkSelect((ArrayExpr)terms[read.Map.Name], terms[read.Key.Name]));
}
