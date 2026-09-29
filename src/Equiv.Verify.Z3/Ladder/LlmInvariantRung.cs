using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using Relation = Equiv.Verify.Z3.ChcEncoder.Relation;
using Rung = Equiv.Verify.Z3.LoopLadder.Rung;

namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// Rung 5 of the loop ladder (VERIFICATION-MODEL.md section 5.1; ADR 0008; ADR 0036; ticket P1-002), run when rung 4
/// timed out and <c>--invariant-model</c> names a model. It asks <paramref name="proposer"/> for definitions of rung 4's
/// relations (<see cref="ChcEncoder.Relations"/>), at most <see cref="MaxRounds"/> times. Each candidate is parsed with
/// <c>ParseSMTLIB2String</c> against the relation's own arguments, one relation at a time, and may use nothing else; it is
/// then checked against rung 4's clauses read with wrap-around arithmetic (<see cref="ChcArithmetic.WrappingIntegers"/>),
/// which makes a proof one over the bitvectors (ticket P1-001), as three obligations (<see cref="ChcEncoder.Refutes"/>).
/// All three unsatisfiable is <see cref="ProofMethod.LlmInvariant"/> Equivalent with the candidate as the invariant and
/// the model as <see cref="Equivalent.ProposedBy"/>. A parse failure or a failed obligation is a rejection fed back to
/// the proposer with its reason; when the rounds run out or the proposer gives up, the pair is
/// <see cref="UnknownReason.NoInvariant"/>. Each round is one ladder step naming the candidate and Z3's verdict on it.
/// The same rung checks <see cref="TraceInvariantProposer"/>'s candidates (ticket P1-009) with <paramref name="method"/>
/// <see cref="ProofMethod.TraceInvariant"/>; a rejection carries the broken rule's facts, so a proposer can weaken its
/// candidate.
/// </summary>
internal sealed class LlmInvariantRung(Func<Context> createContext, VerificationOptions options, IInvariantProposer proposer, string proposedBy, ProofMethod method)
{
    /// <summary>The most candidates rung 5 asks for.</summary>
    public const int MaxRounds = 3;

    public ImmutableArray<Rung> Prove(IrProcedure old, IrProcedure @new)
    {
        using Context context = createContext();
        ChcEncoder chc = new(context, old, @new, ChcArithmetic.WrappingIntegers, options.CallIdentityMap);
        ImmutableArray<Relation> relations = [.. chc.Relations];
        InvariantRequest request = new(IrText.Dump(old), IrText.Dump(@new), [.. relations.Select(Describe)], []);
        List<Rung> rounds = [];
        for (int round = 1; round <= MaxRounds; round++)
        {
            string at = $"round {round.ToString(CultureInfo.InvariantCulture)}";
            string? candidate = proposer.ProposeAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            if (candidate is null)
            {
                rounds.Add(new Rung(new LadderStep(method, RungOutcome.Inconclusive, $"{at}: {proposedBy} proposed no invariant")));
                break;
            }

            string text = OneLine(candidate);
            (Dictionary<FuncDecl, (Expr[] Parameters, BoolExpr Body)>? definitions, string? error) = Parse(context, relations, candidate);
            ChcEncoder.Refutation? rejection = error is null ? chc.Refutes(definitions!, (uint)options.TimeoutMs) : new ChcEncoder.Refutation(error);
            if (rejection is null)
            {
                Equivalent proved = new(method) { Invariant = text, ProposedBy = proposedBy };
                rounds.Add(LoopLadder.Proved(method, $"{at}: Z3 admitted {proposedBy}'s candidate, every init, step and exit obligation unsatisfiable with wrap-around arithmetic: {text}", proved));
                return [.. rounds];
            }

            rounds.Add(new Rung(new LadderStep(method, RungOutcome.Inconclusive, $"{at}: Z3 rejected {proposedBy}'s candidate {text}: {rejection.Reason}")));
            InvariantRequest.Rejection rejected = new(candidate, rejection.Reason) { Premise = rejection.Premise, Conclusion = rejection.Conclusion };
            request = request with { Rejected = request.Rejected.Add(rejected) };
        }

        string detail = $"no invariant {proposedBy} proposed was admitted in {rounds.Count.ToString(CultureInfo.InvariantCulture)} round(s)";
        rounds[^1] = rounds[^1] with { Verdict = new Unknown(UnknownReason.NoInvariant, detail) };
        return [.. rounds];
    }

    private static InvariantRequest.Relation Describe(Relation relation) =>
        new(relation.Decl.Name.ToString(), [.. relation.Parameters.Select(static p => new InvariantRequest.Variable(p.ToString(), p.Sort.ToString()))]);

    /// <summary><paramref name="text"/> with each run of white space one space.</summary>
    private static string OneLine(string text) => string.Join(' ', text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// The candidate's definition of each relation: the candidate with an assertion of the relation applied to its
    /// arguments appended, parsed with only those arguments (and their uninterpreted sorts) declared, so that a
    /// <c>define-fun</c> of the relation expands to its body over them. A definition that uses any other uninterpreted
    /// symbol is rejected, since it could name a constant of a rule and so couple the definition to that rule; whatever
    /// else the text asserts, the last assertion is a formula over the arguments, and so a sound definition to check.
    /// </summary>
    private static (Dictionary<FuncDecl, (Expr[] Parameters, BoolExpr Body)>? Definitions, string? Error) Parse(Context context, IEnumerable<Relation> relations, string candidate)
    {
        Dictionary<FuncDecl, (Expr[] Parameters, BoolExpr Body)> definitions = [];
        foreach (Relation relation in relations)
        {
            string name = relation.Decl.Name.ToString();
            Expr[] parameters = [.. relation.Parameters];
            FuncDecl[] declared = [.. parameters.Select(static p => p.FuncDecl)];
            Sort[] sorts = [.. parameters.SelectMany(static p => Uninterpreted(p.Sort)).DistinctBy(static s => s.Name.ToString(), StringComparer.Ordinal)];
            BoolExpr[] parsed;
            try
            {
                parsed = context.ParseSMTLIB2String(
                    $"{candidate}\n(assert ({name}{string.Concat(parameters.Select(static p => " " + p))}))",
                    [.. sorts.Select(static s => s.Name)],
                    sorts,
                    [.. declared.Select(static d => d.Name)],
                    declared);
            }
            catch (Z3Exception exception)
            {
                return (null, $"it does not parse as a definition of {name}: {OneLine(exception.Message)}");
            }

            if (parsed is not [.., BoolExpr body])
            {
                return (null, $"it asserts no definition of {name}");
            }

            if (Foreign(body, [.. declared]) is { } symbol)
            {
                return (null, $"its definition of {name} uses {symbol}, which is not one of that relation's arguments");
            }

            definitions[relation.Decl] = (parameters, body);
        }

        return (definitions, null);
    }

    /// <summary>The uninterpreted sorts <paramref name="sort"/> is built from, which the parser must be told of.</summary>
    private static IEnumerable<Sort> Uninterpreted(Sort sort) => sort switch
    {
        ArraySort array => Uninterpreted(array.Domain).Concat(Uninterpreted(array.Range)),
        UninterpretedSort => [sort],
        _ => [],
    };

    /// <summary>The first uninterpreted symbol in <paramref name="term"/> that is not in <paramref name="allowed"/>, else null.</summary>
    private static string? Foreign(Expr term, HashSet<FuncDecl> allowed)
    {
        Stack<Expr> pending = new([term]);
        HashSet<Expr> seen = [];
        while (pending.TryPop(out Expr? next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            if (next.IsApp && next.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED && !allowed.Contains(next.FuncDecl))
            {
                return next.FuncDecl.Name.ToString();
            }

            foreach (Expr argument in next switch { Quantifier quantifier => [quantifier.Body], _ when next.IsApp => next.Args, _ => [] })
            {
                pending.Push(argument);
            }
        }

        return null;
    }
}
