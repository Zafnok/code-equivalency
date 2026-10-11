using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SideTerms = Equiv.Verify.Z3.ProductEncoder.SideTerms;

namespace Equiv.Verify.Z3;

/// <summary>
/// ADR 0037's two queries on an Unknown or a Divergent pair (VERIFICATION-MODEL.md section 6; tickets P1-013 and
/// P1-020), over rung 1's product: the pair with its shared fragments encoded as calls, unrolled
/// <see cref="VerificationOptions.Bound"/> times. Each compares only whether each side returns or throws. <c>newFailures</c> asks for an input on which the legacy side returns and the
/// modern side throws; <c>removedFailures</c> the reverse. A side that reaches an unshared <see cref="IrOpaque"/>, or the
/// bound of a looping pair, has an unknown outcome (ADR 0014). So each asks first on inputs where neither side does: a
/// model whose replay's outcomes are untainted (ADR 0026) is <see cref="RefinementOutcome.Found"/>, a tainted one
/// <see cref="RefinementOutcome.Unknown"/>. Only when that is unsatisfiable does it ask again, letting such a side return or
/// throw: unsatisfiable is <see cref="RefinementOutcome.NoneProved"/>, anything else Unknown. Each query gets the pair's
/// resource limit and timeout. A Divergent is asked a third, <c>resultChanges</c>, under the same two rules
/// (<see cref="ResultChanges"/>; ADR 0037's clarification of 2026-10-11).
/// </summary>
internal sealed class FailureRefinementQuery(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>
    /// The queries, timed: the two failure queries, and with <paramref name="resultChanges"/>, which a Divergent sets, the
    /// third. <paramref name="encodable"/> is whether rung 1 could encode the pair; a pair it could not (irreducible
    /// control flow, a self-call it does not inline) has no product, and every answer is Unknown.
    /// </summary>
    public FailureRefinement Run(IrProcedure old, IrProcedure @new, bool encodable, bool resultChanges = false)
    {
        long started = TimeProvider.System.GetTimestamp();
        FailureRefinement answers = encodable ? Query(old, @new, resultChanges) : Unanswered(resultChanges);
        Stages.StepDone(options, Stages.FailureRefinement, started);
        return answers with { Elapsed = TimeProvider.System.GetElapsedTime(started) };
    }

    /// <summary>Every answer Unknown.</summary>
    private static FailureRefinement Unanswered(bool resultChanges) =>
        new(RefinementResult.Unknown, RefinementResult.Unknown) { ResultChanges = resultChanges ? RefinementResult.Unknown : null };

    private FailureRefinement Query(IrProcedure old, IrProcedure @new, bool resultChanges)
    {
        (old, @new, _) = ProductEncoder.ShareFragments(old, @new);
        bool looping = new[] { IrLoopAnalysis.Of(old), IrLoopAnalysis.Of(@new) }.Any(static s => s.IsSelfRecursive || !s.Loops.IsEmpty);
        if (Stages.Timed(options, Stages.Unroll, () => LoopLadder.Unrolled(old, @new, options.Bound)) is not var (oldUnrolled, newUnrolled))
        {
            // Rung 1 refused the same pair, so the caller says it is not encodable; a caller that asks anyway gets no answer.
            return Unanswered(resultChanges);
        }

        return Stages.WithContext(options, createContext, context =>
        {
            ProductEncoding encoding = Stages.Timed(options, Stages.Encode, () => ProductEncoder.Encode(context, oldUnrolled, newUnrolled, options.CallIdentityMap));
            Pair pair = new(context, encoding, oldUnrolled, newUnrolled, looping);
            return new FailureRefinement(
                Fails(pair, (encoding.Old, encoding.OpaqueOld), (encoding.New, encoding.OpaqueNew)),
                Fails(pair, (encoding.New, encoding.OpaqueNew), (encoding.Old, encoding.OpaqueOld)))
            {
                ResultChanges = resultChanges ? ResultChanges(pair) : null,
            };
        });
    }

    /// <summary>
    /// Whether some input makes both sides return, or both throw the same exception type, with another compared observable
    /// differing (ticket P1-020 criterion 3). Found needs a model on which neither side reaches an unshared opaque node or
    /// the bound, whose replay, which ends as the model does, has an untainted outcome on both sides and differs in an
    /// observable untainted on both (<see cref="ModelDecoder.Compare"/>). None-proved needs the query unsatisfiable when a
    /// side past an unshared opaque node, or past the bound, may end any way with any result: no input may reach one at all.
    /// </summary>
    private RefinementResult ResultChanges(Pair pair)
    {
        (Context context, ProductEncoding encoding, IrProcedure old, IrProcedure @new, bool looping) = pair;
        BoolExpr[] withinBound = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        BoolExpr changed = context.MkAnd(
            context.MkOr(
                context.MkAnd(encoding.Old.Returned, encoding.New.Returned),
                context.MkAnd(encoding.Old.Threw, encoding.New.Threw, context.MkEq(encoding.Old.ExceptionType, encoding.New.ExceptionType))),
            encoding.Differs);
        using SolverQuery modelled = Z3Backend.Query(
            context, encoding, options, [changed, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. withinBound]);
        switch (modelled.Check(options, "result-modelled"))
        {
            case Status.SATISFIABLE:
                (Counterexample runs, ModelDecoder.Difference difference) = Stages.Timed(options, Stages.Replay, () => ModelDecoder.Compared(context, modelled.Model, encoding, old, @new));
                return difference == ModelDecoder.Difference.Real & !runs.Old.Taint.Outcome & !runs.New.Taint.Outcome
                    ? new RefinementResult(RefinementOutcome.Found, runs)
                    : RefinementResult.Unknown;
            case Status.UNKNOWN:
                return RefinementResult.Unknown;
        }

        using SolverQuery resolved = Z3Backend.Query(
            context,
            encoding,
            options,
            [context.MkOr(changed, encoding.OpaqueOld, encoding.OpaqueNew, encoding.Old.Unreachable, encoding.New.Unreachable), .. looping ? [] : withinBound]);
        return resolved.Check(options, "result-resolved") == Status.UNSATISFIABLE ? RefinementResult.NoneProved : RefinementResult.Unknown;
    }

    /// <summary>Whether some input makes <paramref name="returns"/> return and <paramref name="fails"/> throw.</summary>
    private RefinementResult Fails(Pair pair, (SideTerms Terms, BoolExpr Opaque) returns, (SideTerms Terms, BoolExpr Opaque) fails)
    {
        (Context context, ProductEncoding encoding, IrProcedure old, IrProcedure @new, bool looping) = pair;
        BoolExpr[] withinBound = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        using SolverQuery modelled = Z3Backend.Query(
            context, encoding, options, [returns.Terms.Returned, fails.Terms.Threw, context.MkNot(returns.Opaque), context.MkNot(fails.Opaque), .. withinBound]);
        switch (modelled.Check(options, "failure-modelled"))
        {
            case Status.SATISFIABLE:
                Counterexample runs = Stages.Timed(options, Stages.Replay, () => ModelDecoder.Runs(context, modelled.Model, encoding, old, @new));
                return runs.Old.Taint.Outcome || runs.New.Taint.Outcome ? RefinementResult.Unknown : new RefinementResult(RefinementOutcome.Found, runs);
            case Status.UNKNOWN:
                return RefinementResult.Unknown;
        }

        // Unsatisfiable on every modelled input: now let a side past an unshared opaque node, or past the bound, do either.
        using SolverQuery resolved = Z3Backend.Query(
            context,
            encoding,
            options,
            [context.MkOr(returns.Terms.Returned, returns.Opaque, returns.Terms.Unreachable), context.MkOr(fails.Terms.Threw, fails.Opaque, fails.Terms.Unreachable), .. looping ? [] : withinBound]);
        return resolved.Check(options, "failure-resolved") == Status.UNSATISFIABLE ? RefinementResult.NoneProved : RefinementResult.Unknown;
    }

    /// <summary>The product the queries share, the unrolled procedures its models replay through, and whether the pair loops.</summary>
    private sealed record Pair(Context Context, ProductEncoding Encoding, IrProcedure Old, IrProcedure New, bool Looping);
}
