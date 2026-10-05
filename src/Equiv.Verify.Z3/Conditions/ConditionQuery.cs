using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3.Conditions;

/// <summary>
/// The checker of ADR 0048 (VERIFICATION-MODEL.md sections 5 and 6; ticket P1-022): on a pair that is not Equivalent, the
/// input condition under which it is. Each of <see cref="CandidateHarvest"/>'s candidates is a hypothesis (ADR 0036). A
/// candidate <c>p</c> is admitted when both hold on rung 1's product, the pair with its shared fragments encoded as calls:
/// <c>p</c> with "some observable differs or a side reaches an unshared opaque node" is unsatisfiable, which is ADR 0014's
/// second query on the inputs <c>p</c> holds on, so the claim is a proof; and <c>p</c> is satisfiable with the product's
/// own assertions, so some input meets it. A query the solver gives up on admits nothing. The condition is the disjunction
/// of the admitted candidates, without any that implies another. Each query gets the pair's resource limit and timeout.
/// </summary>
internal sealed class ConditionQuery(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>Above this many admitted candidates, none is checked for implying another (the ticket's Design).</summary>
    public const int MaxImplicationChecked = 6;

    /// <summary>
    /// The search on a pair whose divergence has the model <paramref name="counterexample"/>, timed; or null for a pair that
    /// is not searched: one with no model to check the condition against. A pair with a loop or a self-call is not: rung 1's product proves nothing past the bound. Neither is
    /// one whose model calls a runtime-changed member, which is every EQ006: that callee's functions are each side's own,
    /// so nothing would be admitted.
    /// </summary>
    public ConditionSearch? Run(IrProcedure old, IrProcedure @new, Counterexample? counterexample)
    {
        long started = Stages.Start();
        (old, @new, _) = ProductEncoder.ShareFragments(old, @new);
        bool looping = new[] { IrLoopAnalysis.Of(old), IrLoopAnalysis.Of(@new) }.Any(static s => s.IsSelfRecursive || !s.Loops.IsEmpty);
        if (counterexample is null || looping || counterexample.Old.Trace.Concat(counterexample.New.Trace).Any(static r => r.Callee.RuntimeChanged))
        {
            return null;
        }

        ConditionSearch search = Stages.WithContext(options, createContext, context =>
        {
            ProductEncoding encoding = Stages.Timed(options, Stages.Encode, () => ProductEncoder.Encode(context, old, @new, options.CallIdentityMap));
            ImmutableArray<SharedParameter> shared = [.. encoding.Inputs.Select(static i => i.Shared)];
            return Search(context, encoding, CandidateHarvest.Of(old, @new, shared), counterexample);
        });
        Stages.StepDone(options, Stages.Conditions, started);
        return search with { Elapsed = TimeProvider.System.GetElapsedTime(started) };
    }

    /// <summary>
    /// The condition <paramref name="candidates"/> give on <paramref name="encoding"/>. ADR 0048 decision 7: a
    /// <paramref name="counterexample"/> that satisfies it contradicts its proof, and then there is none to report.
    /// </summary>
    internal ConditionSearch Search(Context context, ProductEncoding encoding, ImmutableArray<ConditionTerm> candidates, Counterexample counterexample)
    {
        ImmutableArray<ConditionTerm> admitted = Minimal(context, encoding, [.. candidates.Where(c => Admits(context, encoding, c))]);
        if (admitted.IsEmpty)
        {
            return new ConditionSearch(AgreesWhen: null);
        }

        ImmutableArray<SharedParameter> shared = [.. encoding.Inputs.Select(static i => i.Shared)];
        return Falsifies(context, encoding, admitted, counterexample)
            ? new ConditionSearch(new AgreesWhen(ConditionText.Smt(admitted, shared), ConditionText.Source(admitted, shared)))
            : new ConditionSearch(AgreesWhen: null) { Contradicted = true };
    }

    /// <summary>The two checks of ADR 0048 decision 3, the proof first: most candidates fail it, and it is the one that matters.</summary>
    private bool Admits(Context context, ProductEncoding encoding, ConditionTerm candidate)
    {
        BoolExpr holds = (BoolExpr)candidate.ToExpr(context, encoding);
        BoolExpr[] reachable = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        using Solver proof = Z3Backend.Query(context, encoding, options, [holds, context.MkOr(encoding.Differs, encoding.OpaqueOld, encoding.OpaqueNew), .. reachable]);
        if (Z3Backend.Check(context, proof, options, "condition-proof") != Status.UNSATISFIABLE)
        {
            return false;
        }

        using Solver met = Z3Backend.Query(context, encoding, options, [holds, .. reachable]);
        return Z3Backend.Check(context, met, options, "condition-met") == Status.SATISFIABLE;
    }

    /// <summary>
    /// <paramref name="admitted"/> without each candidate that implies another one still kept: the other covers every input
    /// it does, so the disjunction is the same without it. One query per ordered pair, and none above
    /// <see cref="MaxImplicationChecked"/> candidates.
    /// </summary>
    private ImmutableArray<ConditionTerm> Minimal(Context context, ProductEncoding encoding, ImmutableArray<ConditionTerm> admitted)
    {
        if (admitted.Length > MaxImplicationChecked)
        {
            return admitted;
        }

        List<ConditionTerm> kept = [.. admitted];
        foreach (ConditionTerm candidate in admitted)
        {
            if (kept.Exists(other => other != candidate && Implies(context, encoding, candidate, other)))
            {
                kept.Remove(candidate);
            }
        }

        return [.. kept];
    }

    private bool Implies(Context context, ProductEncoding encoding, ConditionTerm candidate, ConditionTerm other) =>
        Refuted(context, "condition-implies", [(BoolExpr)candidate.ToExpr(context, encoding), context.MkNot((BoolExpr)other.ToExpr(context, encoding))]);

    /// <summary>Whether <paramref name="counterexample"/>'s inputs make every disjunct of <paramref name="admitted"/> false.</summary>
    private bool Falsifies(Context context, ProductEncoding encoding, ImmutableArray<ConditionTerm> admitted, Counterexample counterexample)
    {
        BoolExpr[] inputs = [.. encoding.Inputs.Select((input, i) => context.MkEq(input.Term, encoding.Sorts.Literal(counterexample.Inputs.Arguments[i])))];
        BoolExpr[] disjuncts = [context.MkFalse(), .. admitted.Select(a => (BoolExpr)a.ToExpr(context, encoding))];
        return Refuted(context, "condition-counterexample", [context.MkOr(disjuncts), .. inputs, .. encoding.Sorts.Distinctness()]);
    }

    /// <summary>Whether <paramref name="constraints"/>, which are over the inputs alone, are unsatisfiable together.</summary>
    private bool Refuted(Context context, string query, BoolExpr[] constraints)
    {
        using Solver solver = context.MkSolver();
        Z3Backend.Limit(solver, options);
        solver.Add(constraints);
        return Z3Backend.Check(context, solver, options, query) == Status.UNSATISFIABLE;
    }
}
