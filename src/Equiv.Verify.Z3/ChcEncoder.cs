using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using Microsoft.Z3;

using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 4's constrained Horn clauses (VERIFICATION-MODEL.md section 5.1; ADR 0008; ticket P1-001), after Felsing,
/// Grebing, Klebanov, Rümmer and Ulbrich, "Automating regression verification" (ASE 2014). Each side is cut at every loop
/// header (<see cref="IrFragmenter.Segment"/>), so its cut points are its entry, its headers and its exit, and a segment
/// runs from the entry or a header to the next cut point. One relation <c>inv.&lt;a&gt;.&lt;b&gt;</c> per old cut point
/// <c>a</c> and new cut point <c>b</c> other than the entries holds the shared inputs, the sort literals (so that each keeps
/// one identity along a derivation), the old side's state at <c>a</c> and the new side's at <c>b</c>. A header's state is
/// its <see cref="IrFragmenter.State"/> without the parameters, which are inputs, and without the variables a constant
/// defines, which are that literal; the exit's is the side's observables: whether it returned, the value, the exception
/// type's id (0 on a return) and the final value of each by-ref parameter.
/// <para>
/// A step runs a segment on one side or both, as Rêve schedules it: both sides step when both segments return to their
/// header or both leave it; otherwise only the side whose segment returns to its header steps, and a side that has exited
/// waits. So every pair of runs that both terminate is one path of steps from the entries to <c>inv.exit.exit</c>, and the
/// pair is partially equivalent when no derivation reaches <c>bad</c>: both sides exited with different observables, or a
/// side has a segment to run that reaches an <see cref="IrOpaque"/>. The overflow query reaches <c>bad</c> instead when a
/// segment to run overflows an operation <see cref="IntModeTranslator"/> models exactly, and an integer-mode answer can be
/// checked against the clauses read with wrap-around arithmetic (<see cref="Solves"/>). No fragment calls or applies a
/// pure function: rung 4 does not apply to such a pair. Every rule is universally quantified over its constants.
/// </para>
/// </summary>
internal sealed class ChcEncoder
{
    private readonly Context context;
    private readonly SortMapper sorts;
    private readonly Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
    private readonly Cuts old;
    private readonly Cuts @new;
    private readonly ImmutableArray<Expr> literals;
    private readonly ImmutableArray<IrSortValue> literalValues;
    private readonly FuncDecl bad;
    private readonly List<BoolExpr> entryDivergence = [];
    private readonly Horn divergence;
    private readonly Horn overflow;

    /// <param name="context">
    /// The context every term lives in. Encoders of one pair may share it: those of the same arithmetic then declare the
    /// same relations.
    /// </param>
    /// <param name="old">The old procedure: no call, no pure function, no self-call.</param>
    /// <param name="new">The new procedure, likewise.</param>
    /// <param name="arithmetic">How a bitvector reads (<see cref="IntModeTranslator"/>).</param>
    /// <param name="callIdentityMap">The call-identity unifications <see cref="Calls"/> renames legacy traces with.</param>
    public ChcEncoder(Context context, IrProcedure old, IrProcedure @new, ChcArithmetic arithmetic, ImmutableDictionary<string, string> callIdentityMap)
    {
        this.context = context;
        sorts = new SortMapper(context, arithmetic == ChcArithmetic.BitVectors ? null : new IntModeTranslator(context, wraps: arithmetic == ChcArithmetic.WrappingIntegers));
        Inputs = [.. ProductEncoder.Pair(old, @new).Select(s => (s, context.MkConst(s.InputName, sorts.Sort(s.Type))))];
        this.old = new Cuts(this, Side.Old, old);
        this.@new = new Cuts(this, Side.New, @new);
        literalValues = [.. sorts.SortLiterals.Keys];
        literals = [.. sorts.SortLiterals.Values];
        Calls = new TraceEncoder(sorts, [], callIdentityMap, []);
        bad = context.MkFuncDecl("bad", [], context.BoolSort);
        divergence = Encode(new Horn(arithmetic == ChcArithmetic.WrappingIntegers ? sorts.Integers : null, Overflows: false));
        divergence.Rules.Add(Rule([.. Premise(divergence, a: null, b: null), Differs(this.old.Observables(this.old.Pre(point: null)), this.@new.Observables(this.@new.Pre(point: null)))], Bad));
        overflow = arithmetic == ChcArithmetic.Integers ? Encode(new Horn(sorts.Integers, Overflows: true)) : new Horn(Bounds: null, Overflows: true);
    }

    /// <summary>The shared inputs in <see cref="ProductEncoder.Pair"/> order, each with its constant.</summary>
    public ImmutableArray<(SharedParameter Shared, Expr Term)> Inputs { get; }

    /// <summary>A trace encoder of this context, only for renaming legacy call identities when a replay is compared.</summary>
    public TraceEncoder Calls { get; }

    /// <summary>What every relation carries unchanged: the shared inputs, then the sort literals.</summary>
    private IEnumerable<Expr> Carried => Inputs.Select(static i => i.Term).Concat(literals);

    private BoolExpr Bad => (BoolExpr)context.MkApp(bad);

    /// <summary>
    /// Asks Spacer, within <paramref name="timeoutMs"/>, whether <c>bad</c> is derivable: through the product's steps and
    /// divergence rules, or, when <paramref name="overflows"/> is set, through the same steps from states within bounds
    /// (<see cref="Premise"/>) and the overflow rules. Global guidance (Krishnan et al., CAV 2020) keeps Spacer from enumerating counter values one lemma at a time; its
    /// concretize rule is off because with it a timeout throws "unreachable" instead of cancelling. Inlining and slicing
    /// are off so that every relation a derivation passes through leaves a ground fact of that relation with every argument
    /// (<see cref="DerivationInputs"/>): Z3 inlines a loop pair whose self-steps it simplified away (a loop whose guard is
    /// always false), and slicing replaces a relation by a copy without the inputs no rule reads. A timeout is
    /// <see cref="Status.UNKNOWN"/>: Z3 reports it by throwing an exception, whose message says "canceled", or under global
    /// guidance sometimes "unreachable" (after printing an assertion violation); any exception Z3 throws gives up the same way.
    /// </summary>
    public ChcAnswer Query(bool overflows, uint timeoutMs)
    {
        using Fixedpoint fixedpoint = context.MkFixedpoint();
        using Params parameters = context.MkParams();
        parameters.Add("engine", "spacer");
        parameters.Add("timeout", timeoutMs);
        parameters.Add("spacer.global", value: true);
        parameters.Add("spacer.gg.concretize", value: false);
        parameters.Add("spacer.ground_pobs", value: false);
        Horn system = overflows ? overflow : divergence;
        parameters.Add("spacer.q3.use_qgen", system.HasMaps);
        parameters.Add("xform.inline_eager", value: false);
        parameters.Add("xform.inline_linear", value: false);
        parameters.Add("xform.slice", value: false);
        fixedpoint.Parameters = parameters;
        foreach (FuncDecl relation in system.Relations.Values.Append(bad))
        {
            fixedpoint.RegisterRelation(relation);
        }

        foreach (BoolExpr rule in system.Rules)
        {
            fixedpoint.AddRule(context.MkForall(Constants(rule), rule));
        }

        try
        {
            Status status = fixedpoint.Query(Bad);
            return new ChcAnswer(status, fixedpoint.GetAnswer(), fixedpoint.GetReasonUnknown());
        }
        catch (Z3Exception exception)
        {
            return new ChcAnswer(Status.UNKNOWN, context.MkTrue(), exception.Message);
        }
    }

    /// <summary>
    /// The coupling invariant of an unsatisfiable divergence query's answer: each relation Spacer defines as something other
    /// than false (an unreachable pair of cut points), in the order of the cut points, as
    /// <c>old &lt;a&gt; ~ new &lt;b&gt;: &lt;definition&gt;</c>, with its arguments named after the inputs (<c>in.*</c>),
    /// the sort literals (<c>lit.*</c>) and each side's state (<c>old.*</c>, <c>new.*</c>). Each definition prints on one
    /// line in SMT-LIB, without let-bindings (the context's print mode, which only rung 4's own context gets).
    /// </summary>
    public string Invariant(Expr answer)
    {
        Dictionary<FuncDecl, Definition> definitions = Definitions(answer).ToDictionary(static d => d.Relation);
        context.PrintMode = Z3_ast_print_mode.Z3_PRINT_SMTLIB_FULL;
        return string.Join("; ", divergence.Relations
            .Where(r => definitions.TryGetValue(r.Value, out Definition? definition) && !definition.Body.IsFalse)
            .Select(r => $"old {Label(r.Key.Old)} ~ new {Label(r.Key.New)}: {OneLine(Apply(definitions[r.Value], [.. Inputs.Select(static i => i.Term), .. literals, .. old.Named(r.Key.Old), .. @new.Named(r.Key.New)]))}"));
    }

    /// <summary>
    /// Whether <paramref name="answer"/>, the definitions an unsatisfiable divergence query of an integer-mode encoder of
    /// the same pair and context gave, also solve this wrapping encoder's divergence query
    /// (<see cref="ChcArithmetic.WrappingIntegers"/>): with every relation replaced by its definition, and <c>bad</c> by
    /// false, no rule has a counterexample within <paramref name="timeoutMs"/>. Then no derivation reaches <c>bad</c> over
    /// the bitvectors, whether or not an integer operation can overflow: the plain integers only found the invariant. The two
    /// encoders declare the same relations. Spacer's answer leaves out some relations, the unreachable ones and ones no
    /// derivation of <c>bad</c> passes through; each of those reads as false, and as true once a rule concludes it from
    /// premises that hold (a reachable relation), which only weakens premises, so the check stops after at most one round
    /// per relation.
    /// </summary>
    public bool Solves(Expr answer, uint timeoutMs)
    {
        Dictionary<FuncDecl, Definition> definitions = Definitions(answer).ToDictionary(static d => d.Relation);
        HashSet<FuncDecl> reachable = [];
        while (true)
        {
            BoolExpr[] counterexamples = [.. divergence.Rules.Select(rule => context.MkNot(Read(rule, definitions, reachable)))];
            using Solver solver = context.MkSolver();
            solver.Set("timeout", timeoutMs);
            solver.Add(context.MkOr(counterexamples));
            Status status = solver.Check();
            if (status != Status.SATISFIABLE)
            {
                return status == Status.UNSATISFIABLE;
            }

            Model model = solver.Model;
            FuncDecl[] concluded = [.. divergence.Rules.Where((_, i) => model.Eval(counterexamples[i], completion: true).IsTrue).Select(static r => r.Args[1].FuncDecl)];

            if (concluded.Any(r => definitions.ContainsKey(r) || reachable.Contains(r) || r.Equals(bad)))
            {
                return false;
            }

            reachable.UnionWith(concluded);
        }
    }

    /// <summary>
    /// The inputs of a satisfiable query's derivation: the shared inputs and sort literals every relation holds, read from
    /// the first ground fact of a relation in <paramref name="answer"/>. A derivation that reaches no relation derives
    /// <c>bad</c> from a rule without one, which only an entry segment reaching an opaque node has; its inputs come from a
    /// model of that (or of the two entry segments exiting apart, which Z3 would record as an <c>inv.exit.exit</c> fact).
    /// </summary>
    public IrInputs DerivationInputs(Expr answer, uint timeoutMs)
    {
        HashSet<FuncDecl> declared = [.. divergence.Relations.Values];
        if (Applications(answer).FirstOrDefault(f => declared.Contains(f.FuncDecl)) is { } fact)
        {
            return Decode(fact.Args[..Inputs.Length], fact.Args[Inputs.Length..(Inputs.Length + literals.Length)]);
        }

        using Solver solver = context.MkSolver();
        solver.Set("timeout", timeoutMs);
        solver.Add(context.MkOr(entryDivergence));
        solver.Check();
        Model model = solver.Model;
        return Decode([.. Inputs.Select(i => model.Eval(i.Term, completion: true))], [.. literals.Select(l => model.Eval(l, completion: true))]);
    }

    /// <summary>A cut point as the IR text names it: <c>B&lt;n&gt;</c> for a header, <c>exit</c> for the exit.</summary>
    private static string Label(IrBlockId? point) => point is null ? "exit" : "B" + point.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The applications in <paramref name="term"/> outside every quantifier, once each, in the order a depth-first walk
    /// meets them: a proof's ground facts (the rules it cites are quantified), or a rule's atoms.
    /// </summary>
    private static IEnumerable<Expr> Applications(Expr term)
    {
        Stack<Expr> pending = new([term]);
        HashSet<Expr> seen = [];
        while (pending.TryPop(out Expr? next))
        {
            if (!next.IsApp || !seen.Add(next))
            {
                continue;
            }

            yield return next;
            foreach (Expr argument in next.Args.Reverse())
            {
                pending.Push(argument);
            }
        }
    }

    /// <summary>
    /// The definitions in a query's answer, one per relation Spacer defines, each <c>(= (R x1 .. xn) body)</c> under a
    /// quantifier binding its arguments.
    /// </summary>
    private static IEnumerable<Definition> Definitions(Expr answer) =>
        from conjunct in answer.IsAnd ? answer.Args : [answer]
        let equation = conjunct is Quantifier quantifier ? quantifier.Body : conjunct
        where equation.IsEq && equation.Args[0].IsApp
        select new Definition(equation.Args[0].FuncDecl, equation.Args[0].Args, equation.Args[1]);

    /// <summary><paramref name="term"/> as Z3 prints it, each run of white space one space.</summary>
    private static string OneLine(Expr term) => string.Join(' ', term.ToString().Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A definition's body with each argument replaced by the term at its position in <paramref name="actual"/>.</summary>
    private static Expr Apply(Definition definition, Expr[] actual)
    {
        Expr[] substitution = new Expr[actual.Length];
        for (int i = 0; i < actual.Length; i++)
        {
            substitution[definition.Arguments[i].Index] = actual[i];
        }

        return definition.Body.SubstituteVars(substitution);
    }

    /// <summary>
    /// <paramref name="rule"/> with each relation replaced by its definition, or by true when <paramref name="reachable"/>
    /// holds it and false when neither does, and <c>bad</c> by false.
    /// </summary>
    private BoolExpr Read(BoolExpr rule, Dictionary<FuncDecl, Definition> definitions, HashSet<FuncDecl> reachable)
    {
        Expr[] atoms = [.. Applications(rule).Where(a => a.FuncDecl.Equals(bad) || divergence.Relations.ContainsValue(a.FuncDecl))];
        Expr[] meanings =
        [
            .. atoms.Select(a => definitions.TryGetValue(a.FuncDecl, out Definition? definition)
                ? Apply(definition, a.Args)
                : context.MkBool(reachable.Contains(a.FuncDecl))),
        ];
        return (BoolExpr)rule.Substitute(atoms, meanings);
    }

    /// <summary>
    /// Every uninterpreted constant in <paramref name="term"/> other than the nullary relation <c>bad</c>, once, in the order
    /// a depth-first walk meets them.
    /// </summary>
    private Expr[] Constants(Expr term)
    {
        List<Expr> found = [];
        HashSet<Expr> seen = [];
        Stack<Expr> pending = new([term]);
        while (pending.TryPop(out Expr? next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            if (next.IsConst && next.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED && next.FuncDecl != bad)
            {
                found.Add(next);
            }

            foreach (Expr argument in next.Args.Reverse())
            {
                pending.Push(argument);
            }
        }

        return [.. found];
    }

    /// <summary>The inputs and literals a derivation gives, decoded, with each literal keeping its own element.</summary>
    private IrInputs Decode(Expr[] inputs, Expr[] literalTerms)
    {
        ModelDecoder.Values values = new();
        for (int i = 0; i < literalTerms.Length; i++)
        {
            values.Remember(literalValues[i], literalTerms[i]);
        }

        return new IrInputs([.. Inputs.Select((input, i) => values.Decode(inputs[i], input.Shared.Type))]);
    }

    /// <summary>
    /// A rule: <paramref name="body"/> implies <paramref name="head"/>. <see cref="Query"/> quantifies it universally over
    /// its constants; every body has one, a segment's <c>reach</c> or a relation's argument.
    /// </summary>
    private BoolExpr Rule(IEnumerable<BoolExpr> body, BoolExpr head) => context.MkImplies(context.MkAnd(body), head);

    /// <summary>The relation of <paramref name="a"/> and <paramref name="b"/> in <paramref name="system"/> applied to the inputs, the literals and the two states.</summary>
    private BoolExpr Atom(Horn system, IrBlockId? a, IrBlockId? b, IEnumerable<Expr> oldState, IEnumerable<Expr> newState)
    {
        Expr[] arguments = [.. Carried, .. oldState, .. newState];
        return (BoolExpr)context.MkApp(system.Relations[(a, b)], arguments);
    }

    /// <summary>
    /// <paramref name="system"/>'s relations, one per pair of cut points, and the product's rules: the entry step, then the
    /// steps from every pair of cut points but the two exits, and <c>bad</c> whenever a side has a segment to run that
    /// reaches an opaque node (the divergence query) or overflows (the overflow query).
    /// </summary>
    private Horn Encode(Horn system)
    {
        foreach ((IrBlockId? a, IrBlockId? b) in Pairs())
        {
            system.Relations.Add((a, b), context.MkFuncDecl($"inv.{Label(a)}.{Label(b)}", [.. Carried.Concat(old.Pre(a)).Concat(@new.Pre(b)).Select(static t => t.Sort)], context.BoolSort));
        }

        EncodeEntries(system);
        foreach ((IrBlockId? a, IrBlockId? b) in Pairs().Where(static p => p.Old is not null || p.New is not null))
        {
            EncodeSteps(system, a, b);
        }

        return system;
    }

    /// <summary>Every pair of an old and a new cut point, each side's headers in pre-order and then its exit.</summary>
    private IEnumerable<(IrBlockId? Old, IrBlockId? New)> Pairs() => old.Points.SelectMany(a => @new.Points.Select(b => (a, b)));

    /// <summary>
    /// The entry step: both entry segments together, into the pair of cut points they reach; and <c>bad</c> when either
    /// entry segment fails (<see cref="Failure"/>). The divergence query also records each entry rule that reaches
    /// <c>bad</c> without a relation, for <see cref="DerivationInputs"/>.
    /// </summary>
    private void EncodeEntries(Horn system)
    {
        Segment oldEntry = old.Entry;
        Segment newEntry = @new.Entry;
        BoolExpr[] start = [.. Start(system.Bounds)];
        foreach (SegmentExit oldExit in oldEntry.Exits)
        {
            foreach (SegmentExit newExit in newEntry.Exits)
            {
                BoolExpr[] body = [.. start, oldEntry.Formula, newEntry.Formula, oldExit.Reach, newExit.Reach];
                Step(system, body, oldExit.Target, newExit.Target, old.Moved(oldExit), @new.Moved(newExit));
                if (!system.Overflows && oldExit.Target is null && newExit.Target is null)
                {
                    entryDivergence.Add(context.MkAnd(body.Append(Differs(old.Observables(oldExit.Values), @new.Observables(newExit.Values)))));
                }
            }
        }

        foreach (Segment entry in (Segment[])[oldEntry, newEntry])
        {
            BoolExpr[] body = [.. start, entry.Formula, Failure(entry, system)];
            system.Rules.Add(Rule(body, Bad));
            if (!system.Overflows)
            {
                entryDivergence.Add(context.MkAnd(body));
            }
        }
    }

    /// <summary>
    /// What a run starts from: distinct literals of one sort and, with <paramref name="bounds"/>, the bitvector inputs
    /// within their bounds. The plain divergence query needs no bounds: integers out of bounds only add runs, and a
    /// derivation is replayed anyway.
    /// </summary>
    private IEnumerable<BoolExpr> Start(IntModeTranslator? bounds) =>
    [
        .. Inputs.Where(i => bounds is not null && i.Shared.Type is IrBitVec).Select(i => bounds!.InRange(i.Term, ((IrBitVec)i.Shared.Type).Width)),
        .. sorts.Distinctness(),
    ];

    /// <summary>
    /// A step's premise: the relation of <paramref name="a"/> and <paramref name="b"/> holds of the inputs, the literals and
    /// the two states; and in a query with bounds, what every state of a bitvector run keeps: <see cref="Start"/>, and each
    /// bitvector of the two states within its bounds. In the overflow query that holds until an operation first overflows,
    /// so the first overflow of any run is still derivable; with wrap-around arithmetic it always holds.
    /// </summary>
    private BoolExpr[] Premise(Horn system, IrBlockId? a, IrBlockId? b) =>
    [
        Atom(system, a, b, old.Pre(a), @new.Pre(b)),
        .. Kept(system.Bounds),
        .. old.Bounds(a, system.Bounds),
        .. @new.Bounds(b, system.Bounds),
    ];

    /// <summary>With <paramref name="bounds"/>, what <see cref="Start"/> assumes; else nothing.</summary>
    private IEnumerable<BoolExpr> Kept(IntModeTranslator? bounds) => bounds is null ? [] : Start(bounds);

    /// <summary>What makes a segment's run <c>bad</c>: reaching an opaque node, or in the overflow query an overflow.</summary>
    private static BoolExpr Failure(Segment segment, Horn system) => system.Overflows ? segment.Encoder.Overflow : segment.Encoder.Opaque;

    /// <summary>The steps from the pair of cut points <paramref name="a"/> and <paramref name="b"/>, not both exits.</summary>
    private void EncodeSteps(Horn system, IrBlockId? a, IrBlockId? b)
    {
        BoolExpr[] pre = Premise(system, a, b);
        Segment? oldSegment = a is null ? null : old.Loops[a];
        Segment? newSegment = b is null ? null : @new.Loops[b];
        foreach (SegmentExit oldExit in oldSegment?.Exits ?? [])
        {
            foreach (SegmentExit newExit in newSegment?.Exits.Where(n => n.Continues == oldExit.Continues) ?? [])
            {
                Step(system, [.. pre, oldSegment!.Formula, newSegment!.Formula, oldExit.Reach, newExit.Reach], oldExit.Target, newExit.Target, old.Moved(oldExit), @new.Moved(newExit));
            }

            if (oldExit.Continues || newSegment is null)
            {
                Step(system, [.. pre, oldSegment!.Formula, oldExit.Reach, Waits(newSegment)], oldExit.Target, b, old.Moved(oldExit), (@new.Pre(b), []));
            }
        }

        foreach (SegmentExit newExit in newSegment?.Exits.Where(n => n.Continues || oldSegment is null) ?? [])
        {
            Step(system, [.. pre, newSegment!.Formula, newExit.Reach, Waits(oldSegment)], a, newExit.Target, (old.Pre(a), []), @new.Moved(newExit));
        }

        foreach (Segment segment in new[] { oldSegment, newSegment }.OfType<Segment>())
        {
            system.Rules.Add(Rule([.. pre, segment.Formula, Failure(segment, system)], Bad));
        }
    }

    /// <summary>What lets a side wait while the other steps alone: it has exited, or its own segment leaves its header.</summary>
    private BoolExpr Waits(Segment? waiting) =>
        waiting is null ? context.MkTrue() : context.MkAnd(waiting.Formula, context.MkNot(waiting.Continues));

    private void Step(Horn system, BoolExpr[] body, IrBlockId? a, IrBlockId? b, (IReadOnlyList<Expr> Terms, IEnumerable<BoolExpr> Post) oldState, (IReadOnlyList<Expr> Terms, IEnumerable<BoolExpr> Post) newState) =>
        system.Rules.Add(Rule([.. body, .. oldState.Post, .. newState.Post], Atom(system, a, b, oldState.Terms, newState.Terms)));

    /// <summary>
    /// Some observable differs, as <see cref="ProductEncoder"/> compares them: whether each returned, the values when both
    /// did, the exception type, and each by-ref input's final value (a side without the parameter leaves the input as it
    /// was). A call trace is empty: no fragment calls.
    /// </summary>
    private BoolExpr Differs(Observables oldExit, Observables newExit)
    {
        IrType? oldType = old.Procedure.ReturnType;
        IrType? newType = @new.Procedure.ReturnType;
        BoolExpr values = (oldType == newType, oldType) switch
        {
            (false, _) => context.MkNot(context.MkOr(oldExit.Returned, newExit.Returned)),
            (true, null) => context.MkTrue(),
            _ => context.MkImplies(context.MkAnd(oldExit.Returned, newExit.Returned), context.MkEq(oldExit.Value!, newExit.Value!)),
        };
        BoolExpr[] equal =
        [
            context.MkEq(oldExit.Returned, newExit.Returned),
            values,
            context.MkEq(oldExit.Exception, newExit.Exception),
            .. Inputs.Where(static i => i.Shared.ByRef).Select(i => context.MkEq(old.Final(oldExit, i.Shared.Old, i.Term), @new.Final(newExit, i.Shared.New, i.Term))),
        ];
        return context.MkNot(context.MkAnd(equal));
    }

    /// <summary>
    /// A query's status and answer: a refutation when satisfiable, the relations' definitions when unsatisfiable, and
    /// otherwise nothing of use and the reason it gave up.
    /// </summary>
    public sealed record ChcAnswer(Status Status, Expr Answer, string Reason);

    /// <summary>
    /// One query's relations and rules: with <paramref name="Bounds"/> when its premises keep bitvectors within bounds
    /// (<see cref="Premise"/>), and <paramref name="Overflows"/> for the overflow query. Outside the integer mode the
    /// overflow query has no rule, since no operation is read as an integer that can overflow.
    /// </summary>
    private sealed record Horn(IntModeTranslator? Bounds, bool Overflows)
    {
        public Dictionary<(IrBlockId? Old, IrBlockId? New), FuncDecl> Relations { get; } = [];

        public List<BoolExpr> Rules { get; } = [];

        /// <summary>Whether some relation holds a map, which Spacer's quantified lemma generator helps with.</summary>
        public bool HasMaps => Relations.Values.Any(static r => r.Domain.Any(static s => s is ArraySort));
    }

    /// <summary>A relation's definition in a query's answer: its arguments as the bound variables its body names them by, and the body.</summary>
    private sealed record Definition(FuncDecl Relation, Expr[] Arguments, Expr Body);

    /// <summary>
    /// A segment: its encoding, its conjoined assertions, its exits and whether it returns to the header it starts at.
    /// </summary>
    private sealed record Segment(FragmentEncoder Encoder, BoolExpr Formula, ImmutableArray<SegmentExit> Exits, BoolExpr Continues);

    /// <summary>
    /// A way out of a segment: where it is taken, the cut point it reaches (a header, or null for the exit), the state it
    /// carries there (the observables at the exit), and whether that header is the one the segment starts at.
    /// </summary>
    private sealed record SegmentExit(BoolExpr Reach, IrBlockId? Target, ImmutableArray<Expr> Values, bool Continues);

    /// <summary>A side's exit state: whether it returned, the value (null without a return type), the exception id, and each by-ref parameter's final value.</summary>
    private sealed record Observables(BoolExpr Returned, Expr? Value, IntExpr Exception, ImmutableArray<Expr> Finals);

    /// <summary>One side: its cut points, the state each carries, and one encoded segment per entry and header.</summary>
    private sealed class Cuts
    {
        private readonly ChcEncoder chc;
        private readonly Side side;
        private readonly Dictionary<string, Expr> inputs;
        private readonly Dictionary<string, IrValue> constants;
        private readonly ImmutableArray<IrParameter> byRef;
        private readonly Dictionary<IrBlockId, ImmutableArray<IrVar>> states;
        private readonly Dictionary<IrBlockId, ImmutableArray<IrVar>> parts;
        private readonly Dictionary<IrBlockId, ImmutableArray<Expr>> pre = [];
        private readonly ImmutableArray<(string Name, Sort Sort)> exitPart;

        public Cuts(ChcEncoder chc, Side side, IrProcedure procedure)
        {
            this.chc = chc;
            this.side = side;
            Procedure = procedure;
            inputs = chc.Inputs
                .Where(i => (side == Side.Old ? i.Shared.Old : i.Shared.New) is not null)
                .ToDictionary(i => (side == Side.Old ? i.Shared.Old : i.Shared.New)!.Var.Name, static i => i.Term, StringComparer.Ordinal);
            constants = procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().ToDictionary(static c => c.Target.Name, static c => c.Value, StringComparer.Ordinal);
            byRef = [.. procedure.Parameters.Where(static p => p.Kind != IrParameterKind.In)];
            ImmutableArray<IrBlockId> headers = [.. IrLoopAnalysis.Of(procedure).Loops.Select(static l => l.Header)];
            states = headers.ToDictionary(static h => h, h => IrFragmenter.State(procedure, h));
            parts = headers.ToDictionary(static h => h, h => (ImmutableArray<IrVar>)[.. states[h].Where(v => !inputs.ContainsKey(v.Name) && !constants.ContainsKey(v.Name))]);
            exitPart =
            [
                ("returned", chc.context.BoolSort),
                .. procedure.ReturnType is { } type ? [("value", chc.sorts.Sort(type))] : Array.Empty<(string, Sort)>(),
                ("exception", chc.context.IntSort),
                .. byRef.Select(p => (p.Var.Name, chc.sorts.Sort(p.Var.Type))),
            ];
            foreach (IrBlockId header in headers)
            {
                pre[header] = [.. parts[header].Select(v => chc.context.MkConst($"pre.{Prefix}.{v.Name}", chc.sorts.Sort(v.Type)))];
            }

            Points = [.. headers.Cast<IrBlockId?>(), null];
            (IrBlockId Header, ImmutableArray<IrVar> State)[] cuts = [.. headers.Select(h => (h, states[h]))];
            Entry = Encode(start: null, cuts);
            Loops = headers.ToDictionary(static h => h, h => Encode(h, cuts));
        }

        public IrProcedure Procedure { get; }

        /// <summary>The IR type of each observable of the exit state, in order; the exception id has none.</summary>
        private IEnumerable<IrType?> ExitTypes =>
            [new IrBool(), .. Procedure.ReturnType is { } type ? [type] : Array.Empty<IrType?>(), null, .. byRef.Select(static p => (IrType?)p.Var.Type)];

        /// <summary>The headers in pre-order, then the exit (null).</summary>
        public ImmutableArray<IrBlockId?> Points { get; }

        /// <summary>The segment from the entry.</summary>
        public Segment Entry { get; }

        /// <summary>The segment from each header.</summary>
        public Dictionary<IrBlockId, Segment> Loops { get; }

        private string Prefix => ProductEncoder.Prefix(side);

        /// <summary>The state at cut point <paramref name="point"/> as a relation's argument in a step's premise.</summary>
        public ImmutableArray<Expr> Pre(IrBlockId? point) =>
            point is null ? [.. exitPart.Select(p => chc.context.MkConst($"pre.{Prefix}.{p.Name}", p.Sort))] : pre[point];

        /// <summary>The IR type of each term of the state at <paramref name="point"/>; the exception id has none.</summary>
        public IEnumerable<IrType?> Types(IrBlockId? point) => point is null ? ExitTypes : parts[point].Select(static v => (IrType?)v.Type);

        /// <summary>With <paramref name="bounds"/>, each bitvector of the state at <paramref name="point"/> within its bounds; else nothing.</summary>
        public IEnumerable<BoolExpr> Bounds(IrBlockId? point, IntModeTranslator? bounds) =>
            Pre(point)
                .Zip(Types(point))
                .Where(p => bounds is not null && p.Second is IrBitVec)
                .Select(p => bounds!.InRange(p.First, ((IrBitVec)p.Second!).Width));

        /// <summary>The state at <paramref name="point"/> named for an invariant: <c>old.&lt;variable&gt;</c>, or the observable's name at the exit.</summary>
        public IEnumerable<Expr> Named(IrBlockId? point) =>
            point is null
                ? exitPart.Select(p => chc.context.MkConst($"{Prefix}.{p.Name}", p.Sort))
                : parts[point].Select(v => chc.context.MkConst($"{Prefix}.{v.Name}", chc.sorts.Sort(v.Type)));

        /// <summary>The state an exit carries as a step's conclusion: a fresh constant per argument, each equal to the exit's value.</summary>
        public (IReadOnlyList<Expr> Terms, IEnumerable<BoolExpr> Post) Moved(SegmentExit exit)
        {
            Expr[] post = [.. (exit.Target is null ? exitPart.Select(static p => (p.Name, p.Sort)) : parts[exit.Target].Select(v => (v.Name, Sort: chc.sorts.Sort(v.Type))))
                .Select(p => chc.context.MkConst($"post.{Prefix}.{p.Name}", p.Sort))];
            return (post, [.. post.Zip(exit.Values, chc.context.MkEq)]);
        }

        /// <summary>An exit state's terms, read by position.</summary>
        public Observables Observables(IReadOnlyList<Expr> exit)
        {
            int value = Procedure.ReturnType is null ? 0 : 1;
            return new Observables((BoolExpr)exit[0], value == 0 ? null : exit[1], (IntExpr)exit[1 + value], [.. exit.Skip(2 + value)]);
        }

        /// <summary>The final value of by-ref <paramref name="parameter"/> in <paramref name="exit"/>, or <paramref name="input"/> when this side has no such parameter.</summary>
        public Expr Final(Observables exit, IrParameter? parameter, Expr input) =>
            parameter is null ? input : exit.Finals[byRef.IndexOf(parameter)];

        /// <summary>
        /// The segment from <paramref name="start"/> (null for the entry), with its cut events taken out: a cut block keeps
        /// only its throw, so the fragment exits there, and the event's arguments are the state it carries.
        /// </summary>
        private Segment Encode(IrBlockId? start, (IrBlockId Header, ImmutableArray<IrVar> State)[] cuts)
        {
            IrProcedure segment = IrFragmenter.Segment(Procedure, start, cuts);
            Dictionary<IrBlockId, IrCall> cutEvents = segment.Blocks
                .Where(static b => b.Terminator is IrThrow { ExceptionType: IrFragmenter.CutException })
                .ToDictionary(static b => b.Id, static b => (IrCall)b.Instructions[0]);
            IrProcedure stripped = segment with { Blocks = [.. segment.Blocks.Select(b => cutEvents.ContainsKey(b.Id) ? b with { Instructions = [] } : b)] };
            Dictionary<string, Expr> bindings = new(inputs, StringComparer.Ordinal);
            if (start is { } header)
            {
                for (int k = 0; k < states[header].Length; k++)
                {
                    bindings[segment.Parameters[k].Var.Name] = Slot(header, states[header][k]);
                }
            }

            FragmentEncoder encoder = new(side, stripped, chc.sorts, functions: null, bindings, [], chc.exceptionTypes);
            ImmutableArray<SegmentExit> exits =
            [
                .. encoder.Exits.Select(e => cutEvents.TryGetValue(e.Block, out IrCall? cut)
                    ? Cut(encoder, e.Block, cuts[int.Parse(cut.Callee.Value.AsSpan(IrFragmenter.CutEvent.Length), CultureInfo.InvariantCulture)].Header, cut.Args, start)
                    : new SegmentExit(encoder.Terms.Reach[e.Block], Target: null, Exit(encoder, e.Exit), Continues: false)),
            ];
            BoolExpr[] continuing = [chc.context.MkFalse(), .. exits.Where(static e => e.Continues).Select(static e => e.Reach)];
            return new Segment(encoder, chc.context.MkAnd(encoder.Assertions), exits, chc.context.MkOr(continuing));
        }

        /// <summary>The term a segment from <paramref name="header"/> reads for its state variable <paramref name="var"/>.</summary>
        private Expr Slot(IrBlockId header, IrVar var) =>
            inputs.TryGetValue(var.Name, out Expr? input) ? input : Local(header, var);

        /// <summary>A state variable of <paramref name="header"/> that is no input: the literal a constant defines, else its relation argument.</summary>
        private Expr Local(IrBlockId header, IrVar var) =>
            constants.TryGetValue(var.Name, out IrValue? value) ? chc.sorts.Literal(value) : pre[header][parts[header].IndexOf(var)];

        /// <summary>A cut exit to <paramref name="target"/>: the carried values of the target's relation state.</summary>
        private SegmentExit Cut(FragmentEncoder encoder, IrBlockId block, IrBlockId target, ImmutableArray<IrVar> carried, IrBlockId? start)
        {
            ImmutableArray<IrVar> state = states[target];
            return new SegmentExit(
                encoder.Terms.Reach[block],
                target,
                [.. parts[target].Select(v => encoder.Term(carried[state.IndexOf(v)]))],
                Continues: target == start);
        }

        /// <summary>The observables of a return or throw: returned, the value, the exception id, the by-ref finals.</summary>
        private ImmutableArray<Expr> Exit(FragmentEncoder encoder, IrTerminator exit)
        {
            ImmutableArray<IrOut> outs = exit is IrReturn ret ? ret.Outs : ((IrThrow)exit).Outs;
            return
            [
                chc.context.MkBool(exit is IrReturn),
                .. Procedure.ReturnType is { } type ? [ReturnValue(encoder, exit, type)] : Array.Empty<Expr>(),
                chc.context.MkInt(exit is IrThrow thrown ? chc.exceptionTypes[thrown.ExceptionType] : 0),
                .. byRef.Select(p => outs.FirstOrDefault(o => string.Equals(o.Param.Name, p.Var.Name, StringComparison.Ordinal)) is { } @out ? encoder.Term(@out.Final) : inputs[p.Var.Name]),
            ];
        }

        /// <summary>The value a return exit returns; a throw's is a constant nothing constrains, which no comparison reads.</summary>
        private Expr ReturnValue(FragmentEncoder encoder, IrTerminator exit, IrType type) =>
            exit is IrReturn { Value: { } value } ? encoder.Term(value) : chc.context.MkConst($"{Prefix}.none", chc.sorts.Sort(type));
    }
}
