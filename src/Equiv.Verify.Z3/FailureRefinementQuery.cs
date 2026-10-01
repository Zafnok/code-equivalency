using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SideTerms = Equiv.Verify.Z3.ProductEncoder.SideTerms;

namespace Equiv.Verify.Z3;

/// <summary>
/// ADR 0037's two queries on an Unknown pair (VERIFICATION-MODEL.md section 6; ticket P1-013), over rung 1's product: the
/// pair with its shared fragments encoded as calls, unrolled <see cref="VerificationOptions.Bound"/> times. Each compares
/// only whether each side returns or throws. <c>newFailures</c> asks for an input on which the legacy side returns and the
/// modern side throws; <c>removedFailures</c> the reverse. A side that reaches an unshared <see cref="IrOpaque"/>, or the
/// bound of a looping pair, has an unknown outcome (ADR 0014). So each asks first on inputs where neither side does: a
/// model whose replay's outcomes are untainted (ADR 0026) is <see cref="RefinementOutcome.Found"/>, a tainted one
/// <see cref="RefinementOutcome.Unknown"/>. Only when that is unsatisfiable does it ask again, letting such a side return or
/// throw: unsatisfiable is <see cref="RefinementOutcome.NoneProved"/>, anything else Unknown. Each query gets the pair's
/// resource limit and timeout.
/// </summary>
internal sealed class FailureRefinementQuery(Func<Context> createContext, VerificationOptions options)
{
    /// <summary>
    /// Both queries, timed. <paramref name="encodable"/> is whether rung 1 could encode the pair; a pair it could not
    /// (irreducible control flow, a self-call it does not inline) has no product, and both answers are Unknown.
    /// </summary>
    public FailureRefinement Run(IrProcedure old, IrProcedure @new, bool encodable)
    {
        long started = TimeProvider.System.GetTimestamp();
        (RefinementResult newFailures, RefinementResult removedFailures) = encodable
            ? Query(old, @new)
            : (RefinementResult.Unknown, RefinementResult.Unknown);
        return new FailureRefinement(newFailures, removedFailures) { Elapsed = TimeProvider.System.GetElapsedTime(started) };
    }

    private (RefinementResult NewFailures, RefinementResult RemovedFailures) Query(IrProcedure old, IrProcedure @new)
    {
        (old, @new, _) = ProductEncoder.ShareFragments(old, @new);
        bool looping = new[] { IrLoopAnalysis.Of(old), IrLoopAnalysis.Of(@new) }.Any(static s => s.IsSelfRecursive || !s.Loops.IsEmpty);
        IrProcedure oldUnrolled = IrUnroller.Unroll(old, options.Bound);
        IrProcedure newUnrolled = IrUnroller.Unroll(@new, options.Bound);
        using Context context = createContext();
        ProductEncoding encoding = ProductEncoder.Encode(context, oldUnrolled, newUnrolled, options.CallIdentityMap);
        Pair pair = new(context, encoding, oldUnrolled, newUnrolled, looping);
        return (
            Fails(pair, (encoding.Old, encoding.OpaqueOld), (encoding.New, encoding.OpaqueNew)),
            Fails(pair, (encoding.New, encoding.OpaqueNew), (encoding.Old, encoding.OpaqueOld)));
    }

    /// <summary>Whether some input makes <paramref name="returns"/> return and <paramref name="fails"/> throw.</summary>
    private RefinementResult Fails(Pair pair, (SideTerms Terms, BoolExpr Opaque) returns, (SideTerms Terms, BoolExpr Opaque) fails)
    {
        (Context context, ProductEncoding encoding, IrProcedure old, IrProcedure @new, bool looping) = pair;
        BoolExpr[] withinBound = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        using Solver modelled = Z3Backend.Query(
            context, encoding, options, [returns.Terms.Returned, fails.Terms.Threw, context.MkNot(returns.Opaque), context.MkNot(fails.Opaque), .. withinBound]);
        switch (modelled.Check())
        {
            case Status.SATISFIABLE:
                Counterexample runs = ModelDecoder.Runs(context, modelled.Model, encoding, old, @new);
                return runs.Old.Taint.Outcome || runs.New.Taint.Outcome ? RefinementResult.Unknown : new RefinementResult(RefinementOutcome.Found, runs);
            case Status.UNKNOWN:
                return RefinementResult.Unknown;
        }

        // Unsatisfiable on every modelled input: now let a side past an unshared opaque node, or past the bound, do either.
        using Solver resolved = Z3Backend.Query(
            context,
            encoding,
            options,
            [context.MkOr(returns.Terms.Returned, returns.Opaque, returns.Terms.Unreachable), context.MkOr(fails.Terms.Threw, fails.Opaque, fails.Terms.Unreachable), .. looping ? [] : withinBound]);
        return resolved.Check() == Status.UNSATISFIABLE ? RefinementResult.NoneProved : RefinementResult.Unknown;
    }

    /// <summary>The product both queries share, the unrolled procedures its models replay through, and whether the pair loops.</summary>
    private sealed record Pair(Context Context, ProductEncoding Encoding, IrProcedure Old, IrProcedure New, bool Looping);
}
