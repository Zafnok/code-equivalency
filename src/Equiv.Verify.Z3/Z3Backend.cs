using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Equiv.Verify.Z3.Conditions;
using Equiv.Verify.Z3.Contracts;
using Equiv.Verify.Z3.Ladder;
using Equiv.Verify.Z3.Refinement;

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
/// query's terms are built in a <see cref="Context"/> of its own, disposed on every path, and each check runs in another
/// (<see cref="SolverQuery"/>; ticket P2-100). A solver <c>unknown</c> is
/// <see cref="UnknownReason.Timeout"/>; the detail carries the solver's own reason and the limit it hit, the resource limit
/// or the wall-clock backstop (<see cref="Limit"/>; ticket P2-050), or neither when the solver gave up for another reason. A
/// rung 1 query that gave up is first asked again with the pair's hard arithmetic abstracted
/// (<see cref="ArithmeticRefinement"/>; ticket P1-031). Every
/// Unknown carries <see cref="FailureRefinementQuery"/>'s two answers (ADR 0037; ticket P1-035), a timeout included unless
/// <see cref="VerificationOptions.RefineTimeouts"/> is off, as it is in quick mode (ADR 0049; ticket P1-032). A
/// Divergent, and an Unknown whose divergence rests on an abstraction, carries <see cref="ConditionQuery"/>'s input
/// condition when its pair has no loop (ADR 0048).
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

    /// <summary>
    /// How many times <see cref="VerificationOptions.TimeoutMs"/> a query may run before it is interrupted (ticket P2-076
    /// criterion 3). Four is the least the ticket allows. At the default timeout it is 240 s, and the longest any query of
    /// the <c>gitextensions-8522</c> run took to answer sat or unsat was 13.5 s (<c>docs/runs/2026-10-02-pair-time.md</c>).
    /// </summary>
    internal const int InterruptAfterTimeouts = 4;

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
        Verdict verdict = new LoopLadder(createContext, options, Proposer(options)) { Arithmetic = Arithmetic }.Verify(oldBody, newBody);

        // ADR 0037 (tickets P1-013 and P1-035): an Unknown of any reason, a timeout included, says whether either side can
        // fail where the other does not. The verdict stays as it is. An unbound pair never reaches the backend (ADR 0029
        // decision 2). ADR 0049 (ticket P1-032): a pass that does not refine timeouts leaves a timeout as the ladder gave it.
        Verdict refined = verdict is Unknown unknown && (options.RefineTimeouts || unknown.Reason != UnknownReason.Timeout)
            ? unknown with { FailureRefinement = new FailureRefinementQuery(createContext, options).Run(oldBody, newBody, unknown.Ladder[0].Outcome != RungOutcome.NotApplicable) }
            : verdict;

        // ADR 0048 (ticket P1-022): a divergence the solver found, real or resting on an abstraction, says under which
        // inputs the pair is proved Equivalent. The verdict stays as it is.
        ConditionQuery conditions = new(createContext, options);
        return refined switch
        {
            Divergent divergent => divergent with { Conditions = conditions.Run(oldBody, newBody, divergent.Counterexample) },
            Unknown { Reason: UnknownReason.Abstraction } abstraction => abstraction with { Conditions = conditions.Run(oldBody, newBody, abstraction.Candidate) },
            _ => refined,
        };
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
        long searching = Stages.Start();
        ImmutableSortedDictionary<string, CalleeContract> admitted = callees
            .Select(c => (c.Identity, Contract: search.Admit(oldBody, newBody, c)))
            .Where(static c => c.Contract is not null)
            .ToImmutableSortedDictionary(static c => c.Identity, static c => c.Contract!, StringComparer.Ordinal);
        Stages.StepDone(options, Stages.ContractSearch, searching);
        if (admitted.IsEmpty)
        {
            return null;
        }

        LoopLadder ladder = new(createContext, options, Proposer(options)) { Contracts = new CallerContracts(admitted.ToImmutableDictionary(StringComparer.Ordinal), ContractEncoding), Arithmetic = Arithmetic };
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

    /// <summary>
    /// When rung 1 asks its queries with the pair's hard arithmetic abstracted (ticket P1-031): by default after one hit
    /// its budget. Only a test sets another, to pin the timeout the path replaces or to force it on every pair.
    /// </summary>
    internal ArithmeticMode Arithmetic { get; init; }

    private IInvariantProposer? Proposer(VerificationOptions options) => options.InvariantModel is { } model ? proposers(model) : null;

    /// <summary>
    /// A fresh query holding the encoding and <paramref name="query"/>, in a context of its own
    /// (<see cref="SolverQuery"/>; ticket P2-100). Its solver runs <c>solve-eqs</c> first,
    /// which substitutes the definitional equalities away so that both sides' copies of an unchanged
    /// computation become one term, then <c>simplify</c>, <c>propagate-values</c> and <c>solve-eqs</c>
    /// again before <c>smt</c>. The order matters: <c>simplify</c> rewrites <c>(= r (not x))</c> to
    /// <c>(not (= x r))</c>, which <c>solve-eqs</c> no longer reads as a definition, and two copies of a
    /// multiplier left behind a <c>reach</c> variable time out. Z3's default solver is worse here: in
    /// incremental mode it skips preprocessing, and its non-incremental default tactic times out on a
    /// plain diamond. The query itself is added with the definitions already <see cref="Inline"/>d. Making the solver
    /// with the encoding translated into its context, and inlining the query and adding it, are a stage each
    /// (<see cref="Stages"/>). A refined encoding's solver (ADR 0053; ticket P1-030) turns floating point into bit-vectors and
    /// those into propositional logic before <c>smt</c> (<c>fpa2bv</c>, <c>simplify</c>, <c>bit-blast</c>): left to
    /// <c>smt</c>'s own floating-point theory, <c>a * 2.0 = a + a</c> on <c>double</c> costs ten times the resources.
    /// </summary>
    internal static SolverQuery Query(Context context, ProductEncoding encoding, VerificationOptions options, params BoolExpr[] query)
    {
        long started = Stages.Start();
        SolverQuery asked = new(context, solving => Pipeline(solving, options, encoding.Refined), encoding.Assertions);
        Stages.Done(options, Stages.Assert, started);
        started = Stages.Start();
        asked.Add(Inline(context, encoding.Assertions, query));
        Stages.Done(options, Stages.Inline, started);
        return asked;
    }

    /// <summary>
    /// The solver of <see cref="Query"/> in <paramref name="context"/>, within <paramref name="options"/>' limits;
    /// <paramref name="floats"/> for a refined encoding's.
    /// </summary>
    private static Solver Pipeline(Context context, VerificationOptions options, bool floats)
    {
        using Tactic solveEqs = context.MkTactic("solve-eqs");
        using Tactic simplify = context.MkTactic("simplify");
        using Tactic propagate = context.MkTactic("propagate-values");
        using Tactic smt = context.MkTactic("smt");
        using Tactic toBitVectors = context.MkTactic("fpa2bv");
        using Tactic bitBlast = context.MkTactic("bit-blast");
        using Tactic pipeline = floats
            ? context.AndThen(solveEqs, simplify, propagate, solveEqs, toBitVectors, simplify, bitBlast, smt)
            : context.AndThen(solveEqs, simplify, propagate, solveEqs, smt);
        Solver solver = context.MkSolver(pipeline);
        Limit(solver, options);
        return solver;
    }

    /// <summary>
    /// How long a query may run before it is interrupted: <see cref="InterruptAfterTimeouts"/> times
    /// <see cref="VerificationOptions.TimeoutMs"/>, or the longest wait a timer takes when that is longer.
    /// </summary>
    internal static long InterruptAfterMs(VerificationOptions options) => Math.Min((long)InterruptAfterTimeouts * options.TimeoutMs, uint.MaxValue - 1);

    /// <summary>
    /// Runs <paramref name="check"/>, one query in <paramref name="context"/>, and interrupts it
    /// (<see cref="Context.Interrupt"/>) if it is still running after <paramref name="afterMs"/> (ticket P2-076 criterion
    /// 3). The resource limit and the timeout Z3 was given end a query long before that; this ends one that neither did.
    /// An interrupted solver answers unknown, with the reason <c>interrupted</c>, and a fixedpoint gives up with the
    /// reason its own timer leaves, so the query has the result a timeout has. Nothing but the one query is ended: the
    /// rung, the pair and the run go on. An interrupt that is under way when the query returns is waited for, so that
    /// none reaches the context later. An interrupt that throws changes none of this (<see cref="Interrupt"/>).
    /// </summary>
    internal static T Interruptible<T>(Context context, long afterMs, Func<T> check) => Interruptible(context.Interrupt, afterMs, check);

    /// <summary>
    /// <see cref="Interruptible{T}(Context, long, Func{T})"/> with the interrupt given, for tests. A
    /// <see cref="Z3Exception"/> from <paramref name="interrupt"/> goes no further (<see cref="Interrupt"/>).
    /// </summary>
    internal static T Interruptible<T>(Action interrupt, long afterMs, Func<T> check)
    {
        using Timer timer = new(_ => Interrupt(interrupt), state: null, dueTime: afterMs, period: System.Threading.Timeout.Infinite);
        try
        {
            return check();
        }
        finally
        {
            using ManualResetEvent stopped = new(initialState: false);
            timer.Dispose(stopped);
            stopped.WaitOne();
        }
    }

    /// <summary>
    /// Runs <paramref name="interrupt"/> on the timer's thread, where an exception nothing catches ends the process
    /// (ticket P2-112). <see cref="Context.Interrupt"/> interrupts the query and then throws if the context holds an
    /// error, which it does when the query is at that moment giving up (<c>canceled</c>), by this interrupt or by a
    /// limit of its own. The interrupt has been made either way, so the exception says nothing the query's own result
    /// does not, and the query ends with that result.
    /// </summary>
    private static void Interrupt(Action interrupt)
    {
        try
        {
            interrupt();
        }
        catch (Z3Exception)
        {
            // The query's result, read on its own thread, is the answer.
        }
    }

    /// <summary>
    /// <paramref name="query"/> with every definition in <paramref name="assertions"/> substituted in, in
    /// assertion order: a Bool constant asserted alone is <c>true</c>, and <c>(= c t)</c> with a constant
    /// <c>c</c> is <c>t</c> with the earlier definitions substituted. The definitions stay asserted, so
    /// the result is equivalent to the query; what it adds is that both sides' copies of an unchanged
    /// computation are one term before Z3 sees them. Z3 5.1's <c>solve-eqs</c> can instead invert a
    /// definition such as <c>t = u + in.b</c> to eliminate the shared input <c>in.b</c>, which leaves
    /// the two sides as different terms and a self-comparison timing out (ticket M3-027). The terms are
    /// those substituting every definition at every step gives, at a cost that grows with the size of
    /// the assertions and not with its square (<see cref="Definitions"/>; ticket P2-076).
    /// </summary>
    internal static BoolExpr[] Inline(Context context, IEnumerable<BoolExpr> assertions, BoolExpr[] query)
    {
        using Definitions definitions = new();
        foreach (BoolExpr assertion in assertions)
        {
            if (assertion.IsConst)
            {
                definitions.Add(assertion, definitions.Own(context.MkTrue()));
            }
            else if (assertion.IsEq && definitions.Own(assertion.Arg(0)) is { IsConst: true } name)
            {
                definitions.Add(name, definitions.Own(definitions.Apply(definitions.Own(assertion.Arg(1)))));
            }
        }

        return [.. query.Select(q => (BoolExpr)definitions.Apply(q))];
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

    internal static string Timeout(SolverQuery solver, VerificationOptions options) =>
        $"solver returned unknown ({solver.ReasonUnknown}){LimitHit(solver.ReasonUnknown, SolverTimedOut, (uint)options.ResourceLimit, options.TimeoutMs)}";

    /// <summary>
    /// Which limit Z3's <paramref name="reason"/> for giving up says was hit (ticket P2-050 criterion 4), as a suffix of a
    /// detail: <c>wall-clock</c> when it is <paramref name="timedOut"/>, the reason the timer leaves
    /// (<see cref="SolverTimedOut"/> on a solver; a fixedpoint is cancelled instead); <c>resource</c> when it is one of the
    /// two an exhausted <c>rlimit</c> leaves, the same on every run; and nothing for any other reason, such as an
    /// incomplete theory. <paramref name="resourceLimit"/> is the <c>rlimit</c> the query had, which for a Spacer query is
    /// <see cref="ChcEncoder.SpacerResourceLimit"/>.
    /// </summary>
    internal static string LimitHit(string reason, string timedOut, uint resourceLimit, int timeoutMs) => reason switch
    {
        _ when string.Equals(reason, timedOut, StringComparison.Ordinal) => $": wall-clock limit {timeoutMs.ToString(CultureInfo.InvariantCulture)} ms hit",
        Canceled or "max. resource limit exceeded" => $": resource limit {resourceLimit.ToString(CultureInfo.InvariantCulture)} hit",
        _ => string.Empty,
    };

    /// <summary>
    /// Every opaque node some input reaches (ADR 0027 decision 4): those <paramref name="model"/> reaches, then, while the
    /// solver finds an input under <paramref name="constraints"/> that reaches one not listed yet, those that input
    /// reaches. A query that is unsatisfiable or gives up ends the search with what it has.
    /// </summary>
    internal static ImmutableArray<UnknownCause> ReachableOpaques(
        Context context, ProductEncoding encoding, VerificationOptions options, SolverModel model, BoolExpr[] constraints, long? interruptAfterMs = null)
    {
        long started = Stages.Start();
        HashSet<int> reached = [.. Reached(model, encoding)];
        for (BoolExpr[] rest = Unreached(); rest.Length > 0; rest = Unreached())
        {
            using SolverQuery solver = Query(context, encoding, options, [context.MkOr(rest), .. constraints]);
            if (solver.Check(options, "reachable-opaque", interruptAfterMs) != Status.SATISFIABLE)
            {
                break;
            }

            reached.UnionWith(Reached(solver.Model, encoding));
        }

        Stages.StepDone(options, Stages.ReachableOpaques, started);
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
    internal static IEnumerable<int> Reached(SolverModel model, ProductEncoding encoding) =>
        Enumerable.Range(0, encoding.Opaques.Length).Where(i => model.Eval(encoding.Opaques[i].Reach, completion: true).IsTrue);

    /// <summary>
    /// An opaque Unknown's detail: each <c>side: reason</c> once. The lines are in the causes, so moving an opaque node
    /// leaves the detail, and with it the result's fingerprint, unchanged (ADR 0027 decision 4).
    /// </summary>
    internal static string OpaqueReasons(ImmutableArray<UnknownCause> causes) =>
        string.Join("; ", causes.Select(static c => $"{(c.Side == Codebase.Legacy ? "old" : "new")}: {c.Reason}").Distinct(StringComparer.Ordinal));
}
