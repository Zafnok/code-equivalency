using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// <see cref="IVerificationBackend"/> over Z3 for acyclic pairs (ARCHITECTURE.md; VERIFICATION-MODEL.md
/// sections 1, 5 and 6; ADR 0014; ticket M3-001). One <see cref="Context"/> per pair, disposed on every
/// path. A pair with a back edge on either side is <see cref="UnknownReason.Loop"/> before any Z3 call
/// (the M3-002 ladder replaces that branch). Otherwise two queries decide it: some observable differs
/// on an input where neither side reaches an <see cref="IrOpaque"/> gives <see cref="Divergent"/>, with a
/// counterexample replayed in <see cref="IrInterpreter"/>; if not, an input reaching an opaque gives
/// <see cref="UnknownReason.Opaque"/>, and otherwise the pair is <see cref="Equivalent"/>. A solver
/// <c>unknown</c> is <see cref="UnknownReason.Timeout"/>, the only reason Core has for it; the detail
/// carries the solver's own reason, which is not always a timeout.
/// </summary>
public sealed class Z3Backend : IVerificationBackend
{
    private readonly Func<Context> createContext;

    public Z3Backend()
        : this(static () => new Context())
    {
    }

    /// <summary>For tests: supplies the <see cref="Context"/> each verification uses and disposes.</summary>
    internal Z3Backend(Func<Context> createContext)
    {
        this.createContext = createContext;
    }

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(options);

        IrProcedure? looping = new[] { oldBody, newBody }.FirstOrDefault(static p => ProductEncoder.Analyze(p).HasBackEdge);
        if (looping is not null)
        {
            return new Unknown(UnknownReason.Loop, $"{looping.Identity.Value} has a loop; loops need the M3-002 ladder.");
        }

        using Context context = createContext();
        ProductEncoding encoding = ProductEncoder.Encode(context, oldBody, newBody, options.CallIdentityMap);

        using Solver divergence = Query(context, encoding, options, encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew));
        Status status = divergence.Check();
        if (status == Status.SATISFIABLE)
        {
            return new Divergent(ModelDecoder.Replay(context, divergence.Model, encoding, oldBody, newBody));
        }

        if (status == Status.UNKNOWN)
        {
            return Timeout(divergence, options);
        }

        using Solver opaque = Query(context, encoding, options, context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew));
        return opaque.Check() switch
        {
            Status.UNSATISFIABLE => new Equivalent(),
            Status.SATISFIABLE => new Unknown(UnknownReason.Opaque, OpaqueReasons(opaque.Model, encoding)),
            _ => Timeout(opaque, options),
        };
    }

    /// <summary>
    /// A fresh solver holding the encoding and <paramref name="query"/>. It runs <c>solve-eqs</c> first,
    /// which substitutes the definitional equalities away so that both sides' copies of an unchanged
    /// computation become one term, then <c>simplify</c>, <c>propagate-values</c> and <c>solve-eqs</c>
    /// again before <c>smt</c>. The order matters: <c>simplify</c> rewrites <c>(= r (not x))</c> to
    /// <c>(not (= x r))</c>, which <c>solve-eqs</c> no longer reads as a definition, and two copies of a
    /// multiplier left behind a <c>reach</c> variable time out. Z3's default solver is worse here: in
    /// incremental mode it skips preprocessing, and its non-incremental default tactic times out on a
    /// plain diamond.
    /// </summary>
    private static Solver Query(Context context, ProductEncoding encoding, VerificationOptions options, params BoolExpr[] query)
    {
        using Tactic solveEqs = context.MkTactic("solve-eqs");
        using Tactic simplify = context.MkTactic("simplify");
        using Tactic propagate = context.MkTactic("propagate-values");
        using Tactic smt = context.MkTactic("smt");
        using Tactic pipeline = context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
        Solver solver = context.MkSolver(pipeline);
        solver.Set("timeout", (uint)options.TimeoutMs);
        solver.Add(encoding.Assertions);
        solver.Add(query);
        return solver;
    }

    private static Unknown Timeout(Solver solver, VerificationOptions options) =>
        new(UnknownReason.Timeout, $"solver returned unknown ({solver.ReasonUnknown}) with a {options.TimeoutMs.ToString(CultureInfo.InvariantCulture)} ms timeout");

    /// <summary>The opaque nodes the model reaches, as <c>side: reason at path line:column</c>.</summary>
    private static string OpaqueReasons(Model model, ProductEncoding encoding) =>
        string.Join(
            "; ",
            encoding.Opaques
                .Where(o => model.Eval(o.Reach, completion: true).IsTrue)
                .Select(static o => $"{ProductEncoder.Prefix(o.Side)}: {o.Node.Reason} at {o.Node.Span.Path} " +
                    $"{o.Node.Span.StartLine.ToString(CultureInfo.InvariantCulture)}:{o.Node.Span.StartColumn.ToString(CultureInfo.InvariantCulture)}"));
}
