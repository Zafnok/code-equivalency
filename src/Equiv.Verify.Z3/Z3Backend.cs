using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3;

/// <summary>
/// <see cref="IVerificationBackend"/> over Z3 (ARCHITECTURE.md; VERIFICATION-MODEL.md sections 1, 5, 5.1 and 6; ADR
/// 0014; tickets M3-001 and M3-002). Every pair goes through the <see cref="LoopLadder"/>, whose rung 1 is the
/// M3-001 product query on the pair with its loops unrolled: some observable differs on an input where neither side
/// reaches an <see cref="IrOpaque"/> gives <see cref="Divergent"/>, with a counterexample replayed in
/// <see cref="IrInterpreter"/>; if not, an input reaching an opaque gives <see cref="UnknownReason.Opaque"/>. Each
/// query gets its own <see cref="Context"/>, disposed on every path. A solver <c>unknown</c> is
/// <see cref="UnknownReason.Timeout"/>; the detail carries the solver's own reason, which is not always a timeout.
/// </summary>
public sealed class Z3Backend : IVerificationBackend
{
    private readonly Func<Context> createContext;

    public Z3Backend()
        : this(static () => new Context())
    {
    }

    /// <summary>For tests: supplies the <see cref="Context"/> each query uses and disposes.</summary>
    internal Z3Backend(Func<Context> createContext)
    {
        this.createContext = createContext;
    }

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(options);
        return new LoopLadder(createContext, options).Verify(oldBody, newBody);
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
    internal static Solver Query(Context context, ProductEncoding encoding, VerificationOptions options, params BoolExpr[] query)
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

    internal static string Timeout(Solver solver, VerificationOptions options) =>
        $"solver returned unknown ({solver.ReasonUnknown}) with a {options.TimeoutMs.ToString(CultureInfo.InvariantCulture)} ms timeout";

    /// <summary>The opaque nodes the model reaches, as <c>side: reason at path line:column</c>.</summary>
    internal static string OpaqueReasons(Model model, ProductEncoding encoding) =>
        string.Join(
            "; ",
            encoding.Opaques
                .Where(o => model.Eval(o.Reach, completion: true).IsTrue)
                .Select(static o => $"{ProductEncoder.Prefix(o.Side)}: {o.Node.Reason} at {o.Node.Span.Path} " +
                    $"{o.Node.Span.StartLine.ToString(CultureInfo.InvariantCulture)}:{o.Node.Span.StartColumn.ToString(CultureInfo.InvariantCulture)}"));
}
