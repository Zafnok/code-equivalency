using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using TraceComparison = Equiv.Verify.Z3.ProductEncoder.TraceComparison;

namespace ProofCertificatesSpike;

/// <summary>
/// One unsatisfiable query of a proof as an SMT-LIB 2 script, and how it was printed: by <see cref="SecondSolver.Print"/>,
/// or, when that declines a constant array, as Z3 prints it (<see cref="ProofQueries.AsZ3Prints"/>).
/// </summary>
internal sealed record ProofQuery(string Name, string Script, string Printed);

/// <summary>
/// The queries a <c>bounded</c> or <c>lockstep-induction</c> Equivalent rests on, rebuilt as the ladder builds them
/// (<c>LoopLadder.Climb</c>): the pair with its shared fragments made calls, then rung 1's queries on the unrolled
/// product, whose call traces compare by position, or rung 2's base and step obligations on the cut procedures, whose
/// traces compare as sequences. Each query is asked of Z3 with the production solver and limits; only one Z3 answers
/// unsatisfiable is part of the proof at this commit. It is then printed by <see cref="SecondSolver.Print"/>, the
/// exporter P1-025 measured and ADR 0050 made the product's, with <c>produce-proofs</c> in place of
/// <c>produce-models</c> and no <c>get-value</c>.
/// </summary>
internal static class ProofQueries
{
    public const string Bounded = "bounded";
    public const string Lockstep = "lockstep-induction";

    /// <summary>The proof's queries, or why the proof <paramref name="method"/> names is not rebuilt at this commit.</summary>
    public static (List<ProofQuery> Queries, string? Why) Of(string method, IrProcedure old, IrProcedure @new, VerificationOptions options)
    {
        ProductEncoder.SharedFragments shared = ProductEncoder.ShareFragments(old, @new);
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(shared.Old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(shared.New);
        return method == Bounded ? OfBounded(shared.Old, shared.New, oldShape, newShape, options) : OfLockstep(shared.Old, shared.New, oldShape, newShape, options);
    }

    /// <summary>Rung 1 (<c>LoopLadder.Bounded</c> and <c>Queries</c>): divergence, opaque, and for a looping pair the bound.</summary>
    private static (List<ProofQuery>, string?) OfBounded(IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape, VerificationOptions options)
    {
        if (!oldShape.IsReducible || !newShape.IsReducible)
        {
            return ([], "rung 1 does not apply: irreducible control flow");
        }

        if ((IrUnroller.InliningObstacle(old) ?? IrUnroller.InliningObstacle(@new)) is not null)
        {
            return ([], "rung 1 does not apply: self-recursion is not inlined");
        }

        if (LoopLadder.Unrolled(old, @new, options.Bound) is not { } unrolled)
        {
            return ([], "rung 1 does not apply: too large to unroll");
        }

        bool looping = oldShape.IsSelfRecursive || newShape.IsSelfRecursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        using Context context = new();
        ProductEncoding encoding = ProductEncoder.Encode(context, unrolled.Old, unrolled.New, options.CallIdentityMap, traces: TraceComparison.Positional);
        BoolExpr[] reachable = LoopLadder.Reachable(context, encoding);
        List<(string Name, BoolExpr[] Terms)> queries =
        [
            ("divergence", [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable]),
            ("opaque", [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable]),
        ];
        if (looping)
        {
            queries.Add(("bound", [context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable)]));
        }

        List<ProofQuery> exported = [];
        foreach ((string name, BoolExpr[] terms) in queries)
        {
            if (Export(context, encoding, options, name, terms) is not { } query)
            {
                return (exported, $"`{name}` is not unsatisfiable at this commit (the proof is a later round of rung 1)");
            }

            exported.Add(query);
        }

        return (exported, null);
    }

    /// <summary>Rung 2 (<c>LockstepInduction.Prove</c>): the base obligation and one step obligation per paired loop.</summary>
    private static (List<ProofQuery>, string?) OfLockstep(IrProcedure old, IrProcedure @new, IrLoopAnalysis oldShape, IrLoopAnalysis newShape, VerificationOptions options)
    {
        LockstepInduction lockstep = new(new LoopLadder(static () => new Context(), options), old, @new, oldShape, newShape);
        if (lockstep.Misalignment is { } misalignment)
        {
            return ([], $"rung 2 does not apply: {misalignment}");
        }

        // The cuts LockstepInduction keeps to itself, made as its constructor makes them.
        LockstepInduction.Coupling[] couplings = [.. lockstep.Loops.Select(l => LockstepInduction.Couple(old, @new, l.Old, l.New))];
        (IrBlockId Header, System.Collections.Immutable.ImmutableArray<IrVar> State)[] oldCuts = [.. lockstep.Loops.Zip(couplings, static (l, c) => (l.Old, c.Old))];
        (IrBlockId Header, System.Collections.Immutable.ImmutableArray<IrVar> State)[] newCuts = [.. lockstep.Loops.Zip(couplings, static (l, c) => (l.New, c.New))];
        List<(string Name, IrBlockId? Old, IrBlockId? New)> obligations = [("base", null, null)];
        obligations.AddRange(lockstep.Loops.Select(static (l, i) => ($"step {i + 1}", (IrBlockId?)l.Old, (IrBlockId?)l.New)));

        List<ProofQuery> exported = [];
        foreach ((string name, IrBlockId? oldStart, IrBlockId? newStart) in obligations)
        {
            // LoopLadder.Check with no premise and no violation: the fragments agree and reach no opaque.
            using Context context = new();
            ProductEncoding encoding = ProductEncoder.Encode(
                context,
                IrFragmenter.Segment(old, oldStart, oldCuts),
                IrFragmenter.Segment(@new, newStart, newCuts),
                options.CallIdentityMap,
                traces: TraceComparison.Sequence);
            BoolExpr opaque = context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew);
            BoolExpr[] terms = [context.MkTrue(), context.MkOr(encoding.Differs, opaque, context.MkFalse()), .. LoopLadder.Reachable(context, encoding)];
            if (Export(context, encoding, options, name, terms) is not { } query)
            {
                return (exported, $"the {name} obligation is not unsatisfiable at this commit");
            }

            exported.Add(query);
        }

        return (exported, null);
    }

    /// <summary>The query, printed, when Z3 answers it unsatisfiable under the production limits; otherwise null.</summary>
    internal static ProofQuery? Export(Context context, ProductEncoding encoding, VerificationOptions options, string name, BoolExpr[] terms)
    {
        using (SolverQuery solver = Z3Backend.Query(context, encoding, options, terms))
        {
            if (solver.Check(options, name) != Status.UNSATISFIABLE)
            {
                return null;
            }
        }

        if (SecondSolver.Print(context, encoding.Assertions, terms) is { } printed)
        {
            return new ProofQuery(name, ForProof(printed.Script), ByPrint);
        }

        // ADR 0050 decision 2 does not send a constant array whose default is not a value; P1-025 sent it as Z3 prints it,
        // which cvc5 reads under --arrays-exp, with the one rewrite both make.
        using Solver plain = context.MkSolver();
        plain.Add(encoding.Assertions);
        plain.Add(terms);
        return new ProofQuery(name, $"(set-option :produce-proofs true)\n(set-logic ALL)\n{SecondSolver.UnwrapUnaryConcat(plain.ToString())}\n(check-sat)\n", AsZ3Prints);
    }

    public const string ByPrint = "SecondSolver.Print";

    public const string AsZ3Prints = "as Z3 prints it (a constant array kept)";

    /// <summary>The script with proofs asked for in place of models, and no <c>get-value</c>.</summary>
    private static string ForProof(string script) =>
        string.Join('\n', script.Replace("(set-option :produce-models true)", "(set-option :produce-proofs true)", StringComparison.Ordinal)
            .Split('\n')
            .Where(static l => !l.StartsWith("(get-value ", StringComparison.Ordinal)));
}
