using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Equiv.Verify.Z3.Contracts;
using Equiv.Verify.Z3.Ladder;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// <see cref="IVerificationBackend"/> over Z3 (ARCHITECTURE.md; VERIFICATION-MODEL.md sections 1, 5, 5.1 and 6; ADR
/// 0014; tickets M3-001 and M3-002). Every pair goes through the <see cref="LoopLadder"/>, whose rung 1 is the
/// M3-001 product query on the pair with its loops unrolled: some observable differs on an input where neither side
/// reaches an <see cref="IrOpaque"/> gives <see cref="Divergent"/>, with a counterexample replayed in
/// <see cref="IrInterpreter"/>; if not, an input reaching an opaque gives <see cref="UnknownReason.Opaque"/>. Each
/// query gets its own <see cref="Context"/>, disposed on every path. A solver <c>unknown</c> is
/// <see cref="UnknownReason.Timeout"/>; the detail carries the solver's own reason and the limit it hit, the resource limit
/// or the wall-clock backstop (<see cref="Limit"/>; ticket P2-050), or neither when the solver gave up for another reason. Any other
/// Unknown carries <see cref="FailureRefinementQuery"/>'s two answers (ADR 0037).
/// </summary>
public sealed class Z3Backend : IVerificationBackend
{
    /// <summary>The Z3 parameter for the deterministic resource limit of a check or a fixedpoint query.</summary>
    internal const string ResourceLimitParameter = "rlimit";

    /// <summary>The Z3 parameter for the wall-clock limit of a check or a fixedpoint query, in milliseconds.</summary>
    internal const string TimeoutParameter = "timeout";

    /// <summary>A solver's reason for giving up when its timer fired.</summary>
    internal const string SolverTimedOut = "timeout";

    /// <summary>
    /// Z3's reason when a limit cancelled the work in hand: a tactic solver's when <c>rlimit</c> ran out, and a fixedpoint
    /// query's when its timer fired.
    /// </summary>
    internal const string Canceled = "canceled";

    private readonly Func<Context> createContext;
    private readonly Func<string, IInvariantProposer> proposers;

    public Z3Backend()
        : this(static () => new Context())
    {
    }

    /// <summary>
    /// For tests: supplies the <see cref="Context"/> each query uses and disposes, and the proposer rung 5 asks for the
    /// model <see cref="VerificationOptions.InvariantModel"/> names (by default <see cref="AnthropicInvariantProposer"/>).
    /// </summary>
    internal Z3Backend(Func<Context> createContext, Func<string, IInvariantProposer>? proposers = null)
    {
        this.createContext = createContext;
        this.proposers = proposers ?? AnthropicInvariantProposer.FromEnvironment;
    }

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(options);
        Verdict verdict = new LoopLadder(createContext, options, Proposer(options)).Verify(oldBody, newBody);

        // ADR 0037 (ticket P1-013): an Unknown other than a timeout says whether either side can fail where the other does
        // not. The verdict stays as it is. An unbound pair never reaches the backend (ADR 0029 decision 2).
        return verdict is Unknown { Reason: not UnknownReason.Timeout } unknown
            ? unknown with { FailureRefinement = new FailureRefinementQuery(createContext, options).Run(oldBody, newBody, unknown.Ladder[0].Outcome != RungOutcome.NotApplicable) }
            : verdict;
    }

    /// <summary>
    /// ADR 0036 decision 2 (ticket P1-010): for each callee pair, the contract <see cref="ContractSearch"/> admits for this
    /// caller's use of it; then the ladder on the caller with each admitted contract relating its calls to that callee, in the
    /// encoding <see cref="ContractEncoding"/> gives them. An Equivalent names every contract in its
    /// <see cref="Equivalent.ContractsUsed"/>, sorted by callee.
    /// </summary>
    public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(oldBody);
        ArgumentNullException.ThrowIfNull(newBody);
        ArgumentNullException.ThrowIfNull(options);
        ContractSearch search = new(createContext, options);
        ImmutableSortedDictionary<string, CalleeContract> admitted = callees
            .Select(c => (c.Identity, Contract: search.Admit(oldBody, newBody, c)))
            .Where(static c => c.Contract is not null)
            .ToImmutableSortedDictionary(static c => c.Identity, static c => c.Contract!, StringComparer.Ordinal);
        if (admitted.IsEmpty)
        {
            return null;
        }

        LoopLadder ladder = new(createContext, options, Proposer(options)) { Contracts = new CallerContracts(admitted.ToImmutableDictionary(StringComparer.Ordinal), ContractEncoding) };
        if (ladder.Verify(oldBody, newBody) is not Equivalent equivalent)
        {
            return null;
        }

        using Context context = createContext();
        return equivalent with { ContractsUsed = [.. admitted.Select(c => new ContractUse(c.Key, c.Value.Text(context), ObservedPredicates.Name))] };
    }

    /// <summary>
    /// How a caller's product encodes a call under contract: <see cref="FreshPerSideEncoding"/>, each side its own outcome and
    /// heap (ADR 0036 decision 2). Only a test sets another, to show what sharing the call functions would prove.
    /// </summary>
    internal ICalleeContractEncoding ContractEncoding { get; init; } = FreshPerSideEncoding.Instance;

    private IInvariantProposer? Proposer(VerificationOptions options) => options.InvariantModel is { } model ? proposers(model) : null;

    /// <summary>
    /// A fresh solver holding the encoding and <paramref name="query"/>. It runs <c>solve-eqs</c> first,
    /// which substitutes the definitional equalities away so that both sides' copies of an unchanged
    /// computation become one term, then <c>simplify</c>, <c>propagate-values</c> and <c>solve-eqs</c>
    /// again before <c>smt</c>. The order matters: <c>simplify</c> rewrites <c>(= r (not x))</c> to
    /// <c>(not (= x r))</c>, which <c>solve-eqs</c> no longer reads as a definition, and two copies of a
    /// multiplier left behind a <c>reach</c> variable time out. Z3's default solver is worse here: in
    /// incremental mode it skips preprocessing, and its non-incremental default tactic times out on a
    /// plain diamond. The query itself is added with the definitions already <see cref="Inline"/>d.
    /// </summary>
    internal static Solver Query(Context context, ProductEncoding encoding, VerificationOptions options, params BoolExpr[] query)
    {
        using Tactic solveEqs = context.MkTactic("solve-eqs");
        using Tactic simplify = context.MkTactic("simplify");
        using Tactic propagate = context.MkTactic("propagate-values");
        using Tactic smt = context.MkTactic("smt");
        using Tactic pipeline = context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
        Solver solver = context.MkSolver(pipeline);
        Limit(solver, options);
        solver.Add(encoding.Assertions);
        solver.Add(Inline(context, encoding.Assertions, query));
        return solver;
    }

    /// <summary>
    /// <paramref name="query"/> with every definition in <paramref name="assertions"/> substituted in, in
    /// assertion order: a Bool constant asserted alone is <c>true</c>, and <c>(= c t)</c> with a constant
    /// <c>c</c> is <c>t</c> with the earlier definitions substituted. The definitions stay asserted, so
    /// the result is equivalent to the query; what it adds is that both sides' copies of an unchanged
    /// computation are one term before Z3 sees them. Z3 5.1's <c>solve-eqs</c> can instead invert a
    /// definition such as <c>t = u + in.b</c> to eliminate the shared input <c>in.b</c>, which leaves
    /// the two sides as different terms and a self-comparison timing out (ticket M3-027).
    /// </summary>
    internal static BoolExpr[] Inline(Context context, IEnumerable<BoolExpr> assertions, BoolExpr[] query)
    {
        List<Expr> names = [];
        List<Expr> values = [];
        foreach (BoolExpr assertion in assertions)
        {
            if (assertion.IsConst)
            {
                names.Add(assertion);
                values.Add(context.MkTrue());
            }
            else if (assertion.IsEq && assertion.Args[0].IsConst)
            {
                Expr value = assertion.Args[1].Substitute([.. names], [.. values]);
                names.Add(assertion.Args[0]);
                values.Add(value);
            }
        }

        Expr[] from = [.. names];
        Expr[] to = [.. values];
        return [.. query.Select(q => (BoolExpr)q.Substitute(from, to))];
    }

    /// <summary>
    /// Bounds every check of <paramref name="solver"/> twice (ticket P2-050): by Z3's <c>rlimit</c>, a count of the solver's
    /// own steps, so the same query gives up at the same point on every machine and under any load
    /// (<see cref="VerificationOptions.ResourceLimit"/>); and by <c>timeout</c>, the wall-clock backstop behind it
    /// (<see cref="VerificationOptions.TimeoutMs"/>). Every solver this backend creates goes through here.
    /// </summary>
    internal static void Limit(Solver solver, VerificationOptions options)
    {
        solver.Set(ResourceLimitParameter, (uint)options.ResourceLimit);
        solver.Set(TimeoutParameter, (uint)options.TimeoutMs);
    }

    internal static string Timeout(Solver solver, VerificationOptions options) =>
        $"solver returned unknown ({solver.ReasonUnknown}){LimitHit(solver.ReasonUnknown, SolverTimedOut, options)}";

    /// <summary>
    /// Which limit Z3's <paramref name="reason"/> for giving up says was hit (ticket P2-050 criterion 4), as a suffix of a
    /// detail: <c>wall-clock</c> when it is <paramref name="timedOut"/>, the reason the timer leaves
    /// (<see cref="SolverTimedOut"/> on a solver; a fixedpoint is cancelled instead); <c>resource</c> when it is one of the
    /// two an exhausted <c>rlimit</c> leaves, the same on every run; and nothing for any other reason, such as an
    /// incomplete theory.
    /// </summary>
    internal static string LimitHit(string reason, string timedOut, VerificationOptions options) => reason switch
    {
        _ when string.Equals(reason, timedOut, StringComparison.Ordinal) => $": wall-clock limit {options.TimeoutMs.ToString(CultureInfo.InvariantCulture)} ms hit",
        Canceled or "max. resource limit exceeded" => $": resource limit {options.ResourceLimit.ToString(CultureInfo.InvariantCulture)} hit",
        _ => string.Empty,
    };

    /// <summary>
    /// Every opaque node some input reaches (ADR 0027 decision 4): those <paramref name="model"/> reaches, then, while the
    /// solver finds an input under <paramref name="constraints"/> that reaches one not listed yet, those that input
    /// reaches. A query that is unsatisfiable or gives up ends the search with what it has.
    /// </summary>
    internal static ImmutableArray<UnknownCause> ReachableOpaques(Context context, ProductEncoding encoding, VerificationOptions options, Model model, BoolExpr[] constraints)
    {
        HashSet<int> reached = [.. Reached(model, encoding)];
        for (BoolExpr[] rest = Unreached(); rest.Length > 0; rest = Unreached())
        {
            using Solver solver = Query(context, encoding, options, [context.MkOr(rest), .. constraints]);
            if (solver.Check() != Status.SATISFIABLE)
            {
                break;
            }

            reached.UnionWith(Reached(solver.Model, encoding));
        }

        return Causes(encoding, reached);

        BoolExpr[] Unreached() => [.. encoding.Opaques.Where((_, i) => !reached.Contains(i)).Select(static o => o.Reach)];
    }

    /// <summary>
    /// The opaque nodes at <paramref name="indices"/> of <see cref="ProductEncoding.Opaques"/> as causes, each line once
    /// (unrolling copies a node), legacy side first and then by position in the source.
    /// </summary>
    internal static ImmutableArray<UnknownCause> Causes(ProductEncoding encoding, IEnumerable<int> indices) =>
        [.. indices
            .Select(i => encoding.Opaques[i])
            .Select(static o => new UnknownCause(o.Side == Side.Old ? Codebase.Legacy : Codebase.Modern, o.Node.Reason, o.Node.Span))
            .Distinct()
            .OrderBy(static c => c.Side)
            .ThenBy(static c => c.Span.Path, StringComparer.Ordinal)
            .ThenBy(static c => c.Span.StartLine)
            .ThenBy(static c => c.Span.StartColumn)];

    /// <summary>The indices of the opaque nodes <paramref name="model"/> reaches.</summary>
    internal static IEnumerable<int> Reached(Model model, ProductEncoding encoding) =>
        Enumerable.Range(0, encoding.Opaques.Length).Where(i => model.Eval(encoding.Opaques[i].Reach, completion: true).IsTrue);

    /// <summary>
    /// An opaque Unknown's detail: each <c>side: reason</c> once. The lines are in the causes, so moving an opaque node
    /// leaves the detail, and with it the result's fingerprint, unchanged (ADR 0027 decision 4).
    /// </summary>
    internal static string OpaqueReasons(ImmutableArray<UnknownCause> causes) =>
        string.Join("; ", causes.Select(static c => $"{(c.Side == Codebase.Legacy ? "old" : "new")}: {c.Reason}").Distinct(StringComparer.Ordinal));
}
