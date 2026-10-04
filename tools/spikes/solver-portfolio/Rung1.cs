using System.Text;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace SolverPortfolioSpike;

/// <summary>
/// Rung 1 of the ladder, built as <c>LoopLadder.Bounded</c> builds it: the pair with its shared fragments made calls and
/// its loops unrolled to the bound, the product encoding, and the rung's three queries in order. The spike asks them with
/// the production solver and limits, and the first one Z3 gives up on is the query it exports.
/// </summary>
internal sealed record Rung1(IrProcedure Old, IrProcedure New, bool Looping)
{
    public const string Divergence = "divergence";
    public const string Opaque = "opaque";
    public const string Bound = "bound";

    /// <summary>The unrolled pair, or why rung 1 does not apply to it.</summary>
    public static (Rung1? Pair, string? Why) Prepare(IrProcedure old, IrProcedure @new, VerificationOptions options)
    {
        ProductEncoder.SharedFragments shared = ProductEncoder.ShareFragments(old, @new);
        IrLoopAnalysis oldShape = IrLoopAnalysis.Of(shared.Old);
        IrLoopAnalysis newShape = IrLoopAnalysis.Of(shared.New);
        if (!oldShape.IsReducible || !newShape.IsReducible)
        {
            return (null, "rung 1 does not apply: irreducible control flow");
        }

        if ((IrUnroller.InliningObstacle(shared.Old) ?? IrUnroller.InliningObstacle(shared.New)) is not null)
        {
            return (null, "rung 1 does not apply: self-recursion is not inlined");
        }

        bool looping = oldShape.IsSelfRecursive || newShape.IsSelfRecursive || !oldShape.Loops.IsEmpty || !newShape.Loops.IsEmpty;
        return (new Rung1(IrUnroller.Unroll(shared.Old, options.Bound), IrUnroller.Unroll(shared.New, options.Bound), looping), null);
    }

    public ProductEncoding Encode(Context context, VerificationOptions options) =>
        ProductEncoder.Encode(context, Old, New, options.CallIdentityMap);

    /// <summary>The terms of the query <paramref name="name"/>, as <c>LoopLadder.Bounded</c> and <c>WithinBound</c> give them.</summary>
    public static BoolExpr[] Terms(Context context, ProductEncoding encoding, string name)
    {
        BoolExpr[] reachable = [context.MkNot(encoding.Old.Unreachable), context.MkNot(encoding.New.Unreachable)];
        return name switch
        {
            Divergence => [encoding.Differs, context.MkNot(encoding.OpaqueOld), context.MkNot(encoding.OpaqueNew), .. reachable],
            Opaque => [context.MkOr(encoding.OpaqueOld, encoding.OpaqueNew), .. reachable],
            _ => [context.MkOr(encoding.Old.Unreachable, encoding.New.Unreachable)],
        };
    }

    /// <summary>
    /// Asks rung 1's queries in order. <c>TimedOut</c> names the first one Z3 gives up on; otherwise <c>Why</c> says what
    /// rung 1 did instead.
    /// </summary>
    public (string? TimedOut, string? Why) FirstUnknown(Context context, ProductEncoding encoding, VerificationOptions options)
    {
        foreach (string name in (string[])[Divergence, Opaque, Bound])
        {
            if (name == Bound && !Looping)
            {
                return (null, "rung 1 decides the pair at this commit: every query unsatisfiable");
            }

            using Solver solver = Z3Backend.Query(context, encoding, options, Terms(context, encoding, name));
            Status status = Z3Backend.Check(context, solver, options, name);
            if (status == Status.UNKNOWN)
            {
                return (name, null);
            }

            if (status == Status.SATISFIABLE)
            {
                return (null, name == Bound
                    ? "the timeout is on an induction obligation (rungs 2 and 3), which the rung builds itself"
                    : $"rung 1 decides the pair at this commit: {name} satisfiable");
            }
        }

        return (null, "rung 1 decides the pair at this commit: every query unsatisfiable");
    }
}

/// <summary>An exported query: what Z3 prints for the solver's assertions, wrapped as an SMT-LIB 2 script.</summary>
internal sealed partial record SmtFile(string Logic, string Body, IReadOnlyDictionary<string, string> Scalars, IReadOnlyList<string> Z3Only)
{
    /// <summary>
    /// The query <paramref name="name"/> as Z3 prints it: the encoding's assertions and the query's terms in a plain
    /// solver, with their declarations. Nothing is rewritten. The production query adds the terms with the definitions
    /// substituted in (<c>Z3Backend.Inline</c>), which is the same query, since the definitions stay asserted, and prints
    /// tens of times larger.
    /// </summary>
    public static SmtFile Of(Context context, ProductEncoding encoding, string name)
    {
        using Solver solver = context.MkSolver();
        solver.Add(encoding.Assertions);
        solver.Add(Rung1.Terms(context, encoding, name));
        string body = solver.ToString();
        Dictionary<string, string> scalars = new(StringComparer.Ordinal);
        foreach (Match constant in ScalarConstant().Matches(body))
        {
            scalars[constant.Groups[1].Value] = constant.Groups[2].Value;
        }

        return new SmtFile(LogicOf(body), body, scalars, [.. Z3OnlyOperator().Matches(body).Select(static m => m.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    /// <summary>The script: the logic, the assertions, <c>check-sat</c>, and the value of every Bool and bit-vector constant.</summary>
    public string Script()
    {
        StringBuilder script = new();
        script.Append("(set-option :produce-models true)\n(set-logic ").Append(Logic).Append(")\n").Append(Body).Append("\n(check-sat)\n");
        if (Scalars.Count > 0)
        {
            script.Append("(get-value (").AppendJoin(' ', Scalars.Keys).Append("))\n");
        }

        return script.ToString();
    }

    /// <summary>The logic the assertions need, named from the sorts and declarations in them.</summary>
    private static string LogicOf(string body)
    {
        bool quantified = body.Contains("(forall ", StringComparison.Ordinal) || body.Contains("(exists ", StringComparison.Ordinal);
        bool arrays = body.Contains("(Array ", StringComparison.Ordinal);
        bool functions = body.Contains("(declare-sort ", StringComparison.Ordinal) || Function().IsMatch(body);
        bool bitVectors = body.Contains("(_ BitVec ", StringComparison.Ordinal);
        bool datatypes = body.Contains("(declare-datatypes ", StringComparison.Ordinal);
        bool sequences = body.Contains("(Seq ", StringComparison.Ordinal);
        bool integers = Integer().IsMatch(body);
        return (quantified ? string.Empty : "QF_") + (arrays ? "A" : string.Empty) + (functions ? "UF" : string.Empty) + (bitVectors ? "BV" : string.Empty)
            + (datatypes ? "DT" : string.Empty) + (sequences ? "S" : string.Empty) + (integers ? "LIA" : string.Empty) is { Length: > 3 } logic ? logic : "QF_UF";
    }

    /// <summary>
    /// The script with every <c>seq.++</c> of one argument written as that argument. Z3 prints a concatenation of one
    /// sequence, which SMT-LIB's <c>seq.++</c> does not allow and cvc5 rejects. It is the one change the spike makes to
    /// what Z3 prints, and each solver is run on the file both ways.
    /// </summary>
    public string Unwrapped(bool wrap = true)
    {
        const string Concat = "(seq.++";
        string script = wrap ? Script() : Body;
        StringBuilder output = new(script.Length);

        // One frame per open parenthesis: where it starts in the output, whether it is a seq.++, and its arguments so far.
        Stack<(int Start, bool Concat, int Arguments)> open = new();
        bool inAtom = false;
        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            if (c == '(')
            {
                Argument(open);
                bool concat = string.CompareOrdinal(script, i, Concat, 0, Concat.Length) == 0 && char.IsWhiteSpace(script[i + Concat.Length]);
                open.Push((output.Length, concat, -1));
                output.Append(c);
                inAtom = false;
            }
            else if (c == ')')
            {
                (int start, bool concat, int arguments) = open.Pop();
                inAtom = false;
                if (concat && arguments == 1)
                {
                    string argument = output.ToString(start + Concat.Length, output.Length - start - Concat.Length).Trim();
                    output.Length = start;
                    output.Append(argument);
                }
                else
                {
                    output.Append(c);
                }
            }
            else if (c == '|')
            {
                int end = script.IndexOf('|', i + 1);
                if (!inAtom)
                {
                    Argument(open);
                }

                output.Append(script, i, end - i + 1);
                i = end;
                inAtom = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                inAtom = false;
                output.Append(c);
            }
            else
            {
                if (!inAtom)
                {
                    Argument(open);
                    inAtom = true;
                }

                output.Append(c);
            }
        }

        return output.ToString();

        // The head of an application is counted too, so a frame starts at -1.
        static void Argument(Stack<(int Start, bool Concat, int Arguments)> open)
        {
            if (open.TryPop(out (int Start, bool Concat, int Arguments) frame))
            {
                open.Push(frame with { Arguments = frame.Arguments + 1 });
            }
        }
    }

    [GeneratedRegex(@"[ (]Int[ )]")]
    private static partial Regex Integer();

    [GeneratedRegex(@"^\(declare-fun\s+(\|[^|]*\||\S+)\s+\(\)\s+(Bool|\(_ BitVec \d+\))\)\s*$", RegexOptions.Multiline)]
    private static partial Regex ScalarConstant();

    [GeneratedRegex(@"^\(declare-fun\s+(\|[^|]*\||\S+)\s+\([^)]", RegexOptions.Multiline)]
    private static partial Regex Function();

    // Operators Z3 prints that SMT-LIB does not define: its overflow predicates and its internal division forms.
    [GeneratedRegex(@"(?<=\()(bv[su]mul_noovfl|bvsmul_noudfl|bv[su]add_noovfl|bv[su]sub_noovfl|bv[su]div_i|bv[su]rem_i|bvsmod_i|bv[su]div0|bv[su]rem0|bvsmod0)(?= )")]
    private static partial Regex Z3OnlyOperator();
}

/// <summary>
/// Criterion 3: another solver's satisfiable answer counts only if its model, read back, replays. The values it gave the
/// Bool and bit-vector constants are asserted beside the query, Z3 completes the model (the functions, the sorts with no
/// values of their own), and the replay is the one rung 1 runs on a model of its own.
/// </summary>
internal static partial class ReadBack
{
    public const string Divergent = "Divergent";
    public const string Abstraction = "Unknown(abstraction)";

    public static string Run(Context context, ProductEncoding encoding, VerificationOptions options, Rung1 pair, string query, SmtFile file, string values)
    {
        StringBuilder pins = new();
        int pinned = 0;
        foreach (Match value in Value().Matches(values))
        {
            if (file.Scalars.TryGetValue(value.Groups[1].Value, out string? sort))
            {
                pins.Append("(declare-fun ").Append(value.Groups[1].Value).Append(" () ").Append(sort).Append(")\n")
                    .Append("(assert (= ").Append(value.Groups[1].Value).Append(' ').Append(value.Groups[2].Value).Append("))\n");
                pinned++;
            }
        }

        if (pinned == 0)
        {
            return "no model values read";
        }

        BoolExpr[] asserted = context.ParseSMTLIB2String(pins.ToString());
        using Solver solver = Z3Backend.Query(context, encoding, options, [.. Rung1.Terms(context, encoding, query), .. asserted]);
        switch (Z3Backend.Check(context, solver, options, "read-back"))
        {
            case Status.UNSATISFIABLE:
                return "Z3 rejects the model";
            case Status.UNKNOWN:
                return "Z3 cannot complete the model";
        }

        if (query != Rung1.Divergence)
        {
            return query == Rung1.Opaque ? "an input reaches an opaque node" : "an input goes past the bound";
        }

        try
        {
            return ModelDecoder.Replay(context, solver.Model, encoding, pair.Old, pair.New) switch
            {
                Equiv.Core.Verdicts.Divergent => Divergent,
                Unknown { Reason: UnknownReason.Abstraction } => Abstraction,
                _ => "replay gives another verdict",
            };
        }
        catch (InvalidOperationException)
        {
            return "replay shows no difference";
        }
    }

    [GeneratedRegex(@"\((\|[^|]*\||[^\s()]+)\s+(#x[0-9a-fA-F]+|#b[01]+|true|false)\)")]
    private static partial Regex Value();
}
