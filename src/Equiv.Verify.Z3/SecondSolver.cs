using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Equiv.Core;
using Equiv.Core.Verdicts;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;

namespace Equiv.Verify.Z3;

/// <summary>
/// Rung 1's queries on one encoding, each asked of Z3 and, when Z3 gives up and <see cref="VerificationOptions.Solver"/>
/// is given, of that solver (ADR 0050; ticket P1-033). The query is printed (<see cref="Print"/>) and the answer read:
/// <c>unsat</c> is the query's answer, as Z3's would be. <c>sat</c> is never one. The values it gives the query's Bool
/// and bit-vector constants are asserted beside the query and Z3 is asked again under the same limits, which completes
/// the functions; only a model Z3 then gives stands in for the one it did not find, and it is replayed as any other.
/// Anything else (an <c>unknown</c>, a script that cannot be printed exactly, values that are missing or are not
/// values, a solver that throws, a read-back Z3 does not satisfy) leaves the query the timeout it was.
/// </summary>
internal sealed partial class SecondSolver(Context context, ProductEncoding encoding, VerificationOptions options, long? interruptAfterMs)
{
    /// <summary>Whether the second solver answered one of the queries asked so far.</summary>
    public bool Answered { get; private set; }

    /// <summary>Asks the query <paramref name="name"/>: Z3 first, the second solver if Z3 gives up.</summary>
    public Asked Check(string name, params BoolExpr[] query)
    {
        Asked asked = new(context, encoding, options, query);
        if (asked.AskZ3(name, interruptAfterMs) != Status.UNKNOWN || options.Solver is not { } second)
        {
            return asked;
        }

        long started = Stages.Start();
        Printed? printed = Print(context, encoding.Assertions, query);
        SmtAnswer answer = Ask(second, printed, options.TimeoutMs);
        Stages.Checked(options, $"{name}:{second.Name}", started, answer switch { SmtUnsat => Status.UNSATISFIABLE, SmtSat => Status.SATISFIABLE, _ => Status.UNKNOWN });
        if (answer is SmtUnsat)
        {
            Answered = true;
            asked.Refuted();
        }
        else if (answer is SmtSat sat && printed?.Pins(sat.Values) is { } pins)
        {
            Answered |= asked.ReadBack(name + "-read-back", context.ParseSMTLIB2String(pins), interruptAfterMs);
        }

        return asked;
    }

    /// <summary><paramref name="rung"/>, its step naming the second solver and its version when that solver answered one of its queries.</summary>
    public LoopLadder.Rung Tagged(LoopLadder.Rung rung) =>
        Answered ? rung with { Step = rung.Step with { Solver = new SolverUse(options.Solver!.Name, options.Solver.Version) } } : rung;

    /// <summary>
    /// The solver's answer to <paramref name="printed"/>. A query that could not be printed is not sent and is unknown, and
    /// so is one the solver threw on: a solver that fails must not fail the pair.
    /// </summary>
    internal static SmtAnswer Ask(ISmtSolver solver, Printed? printed, int timeoutMs)
    {
        if (printed is null)
        {
            return new SmtUnknown("not sent: a constant array cannot be rewritten exactly");
        }

        try
        {
            return solver.Ask(printed.Script, TimeSpan.FromMilliseconds(timeoutMs));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new SmtUnknown(exception.Message);
        }
    }

    /// <summary>
    /// The query as an SMT-LIB 2 script: the assertions and the query's terms as Z3 prints them from a plain solver (the
    /// inlined form the production query adds prints tens of times larger), after ADR 0050 decision 2's two rewrites,
    /// then <c>check-sat</c> and a <c>get-value</c> over every Bool and bit-vector constant. Null when a constant array
    /// cannot be rewritten exactly (<see cref="WithoutSymbolicConstantArrays"/>).
    /// </summary>
    internal static Printed? Print(Context context, IEnumerable<BoolExpr> assertions, BoolExpr[] query)
    {
        if (WithoutSymbolicConstantArrays(context, [.. assertions, .. query]) is not { } rewritten)
        {
            return null;
        }

        using Solver plain = context.MkSolver();
        plain.Add(rewritten);
        string body = UnwrapUnaryConcat(plain.ToString());
        ImmutableArray<(string Name, string Sort)> scalars = [.. ScalarConstant.Matches(body).Select(static m => (m.Groups["name"].Value, m.Groups["sort"].Value))];
        StringBuilder script = new();
        script.Append("(set-option :produce-models true)\n(set-logic ").Append(LogicOf(body)).Append(")\n").Append(body).Append("\n(check-sat)\n");
        if (!scalars.IsEmpty)
        {
            script.Append("(get-value (").AppendJoin(' ', scalars.Select(static s => s.Name)).Append("))\n");
        }

        return new Printed(script.ToString(), scalars);
    }

    /// <summary>
    /// The first rewrite. Z3 prints a concatenation of one sequence as <c>(seq.++ s)</c>; SMT-LIB's <c>seq.++</c> takes
    /// two or more, and <c>(seq.++ s)</c> is <c>s</c>. Quoted symbols and string literals are copied as they are.
    /// </summary>
    internal static string UnwrapUnaryConcat(string text)
    {
        const string Concat = "(seq.++";
        StringBuilder output = new(text.Length);

        // One frame per open parenthesis: where it starts in the output, whether it is a seq.++, and its arguments so far.
        // The head of an application is counted too, so a frame starts at -1.
        Stack<(int Start, bool Concat, int Arguments)> open = new();
        bool inAtom = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ')')
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

                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                inAtom = false;
                output.Append(c);
                continue;
            }

            if ((c == '(' || !inAtom) && open.TryPop(out (int Start, bool Concat, int Arguments) frame))
            {
                open.Push(frame with { Arguments = frame.Arguments + 1 });
            }

            inAtom = c != '(';
            if (c == '(')
            {
                open.Push((output.Length, string.CompareOrdinal(text, i, Concat, 0, Concat.Length) == 0 && char.IsWhiteSpace(text[i + Concat.Length]), -1));
            }

            // A quoted symbol or a string literal runs to its closing mark, whatever it holds.
            int end = c is '|' or '"' ? text.IndexOf(c, i + 1) : i;
            output.Append(text, i, end - i + 1);
            i = end;
        }

        return output.ToString();
    }

    /// <summary>
    /// The second rewrite. A constant array whose default is not a value, which cvc5 does not read, becomes a fresh array
    /// constant <c>a</c> with <c>(= (select a i) d)</c> asserted for every index <c>i</c> the terms read from an array
    /// built on it. An array is built on it by <c>store</c>, by <c>ite</c>, and by being the constant an asserted
    /// <c>(= c t)</c> defines, when that is the constant's one definition. The fresh array then differs from the constant
    /// one only where nothing reads it, so the terms are satisfiable exactly when the originals are. Any other use of
    /// such an array (an argument of a function, an equality that is not that definition, an element of another array)
    /// can see the whole of it, and the result is null: the query is not sent.
    /// </summary>
    internal static BoolExpr[]? WithoutSymbolicConstantArrays(Context context, BoolExpr[] terms)
    {
        Dictionary<uint, Expr> roots = [];
        Dictionary<uint, List<(Expr Parent, int Index)>> uses = [];
        if (!Collect(terms, roots, uses))
        {
            return null;
        }

        if (roots.Count == 0)
        {
            return terms;
        }

        if (Reads(context, terms, roots, uses) is not { } constraints)
        {
            return null;
        }

        Expr[] from = [.. roots.Values];
        Expr[] to = [.. from.Select(k => context.MkFreshConst("const-array", k.Sort))];
        return [(BoolExpr)context.MkAnd([.. terms, .. constraints]).Substitute(from, to)];
    }

    /// <summary>
    /// Walks <paramref name="terms"/> once: the constant arrays whose default is not a value, as <paramref name="roots"/>,
    /// and every use of an array as an argument, as <paramref name="uses"/>. False when a term is quantified, which the
    /// rewrite does not read.
    /// </summary>
    private static bool Collect(BoolExpr[] terms, Dictionary<uint, Expr> roots, Dictionary<uint, List<(Expr Parent, int Index)>> uses)
    {
        HashSet<uint> seen = [];
        Stack<Expr> pending = new(terms);
        while (pending.TryPop(out Expr? term))
        {
            if (!seen.Add(term.Id))
            {
                continue;
            }

            if (term.IsQuantifier)
            {
                return false;
            }

            if (term.IsConstantArray && !IsValue(term.Arg(0)))
            {
                roots[term.Id] = term;
            }

            Expr[] arguments = term.Args;
            for (int i = 0; i < arguments.Length; i++)
            {
                pending.Push(arguments[i]);
                if (arguments[i].Sort is ArraySort)
                {
                    if (!uses.TryGetValue(arguments[i].Id, out List<(Expr Parent, int Index)>? of))
                    {
                        of = [];
                        uses.Add(arguments[i].Id, of);
                    }

                    of.Add((term, i));
                }
            }
        }

        return true;
    }

    /// <summary>
    /// <c>(= (select k i) d)</c> for every index <c>i</c> read from an array built on the root <c>k</c>, whose default is
    /// <c>d</c>; null when an array built on a root is used in a way that can see more of it than the indices read.
    /// </summary>
    private static List<BoolExpr>? Reads(Context context, BoolExpr[] terms, Dictionary<uint, Expr> roots, Dictionary<uint, List<(Expr Parent, int Index)>> uses)
    {
        // The asserted terms themselves, and how many asserted equalities define each array constant.
        HashSet<uint> asserted = [.. terms.Select(static t => t.Id)];
        Dictionary<uint, int> definitions = terms.Where(static t => t.IsEq && IsArrayConstant(t.Arg(0))).GroupBy(static t => t.Arg(0).Id).ToDictionary(static g => g.Key, static g => g.Count());

        // The roots each array is built on, grown until nothing changes.
        Dictionary<uint, HashSet<uint>> built = roots.Keys.ToDictionary(static id => id, static id => new HashSet<uint> { id });
        HashSet<(uint Root, uint Index)> read = [];
        List<BoolExpr> constraints = [];
        Queue<uint> work = new(roots.Keys);
        while (work.TryDequeue(out uint id))
        {
            foreach ((Expr parent, int index) in uses.GetValueOrDefault(id, []))
            {
                if (parent.IsSelect && index == 0)
                {
                    constraints.AddRange(built[id].Where(root => read.Add((root, parent.Arg(1).Id))).Select(root => context.MkEq(context.MkSelect((ArrayExpr)roots[root], parent.Arg(1)), roots[root].Arg(0))));
                    continue;
                }

                Expr grows;
                if ((parent.IsStore && index == 0) || parent.IsITE)
                {
                    grows = parent;
                }
                else if (parent.IsEq && asserted.Contains(parent.Id) && definitions.GetValueOrDefault(parent.Arg(0).Id) == 1)
                {
                    grows = parent.Arg(0);
                }
                else
                {
                    return null;
                }

                if (grows.Id != id && Grow(built, grows.Id, built[id]))
                {
                    work.Enqueue(grows.Id);
                }
            }
        }

        return constraints;
    }

    /// <summary>Adds <paramref name="roots"/> to those <paramref name="id"/> is built on; whether any was new.</summary>
    private static bool Grow(Dictionary<uint, HashSet<uint>> built, uint id, HashSet<uint> roots)
    {
        if (!built.TryGetValue(id, out HashSet<uint>? on))
        {
            on = [];
            built.Add(id, on);
        }

        int before = on.Count;
        on.UnionWith(roots);
        return on.Count != before;
    }

    private static bool IsArrayConstant(Expr term) =>
        term.IsConst && term.Sort is ArraySort && term.FuncDecl.DeclKind == Z3_decl_kind.Z3_OP_UNINTERPRETED;

    /// <summary>Whether <paramref name="term"/> is a value a constant array may hold by default: a literal, or a constant array of one.</summary>
    private static bool IsValue(Expr term) =>
        term.IsNumeral || term.IsTrue || term.IsFalse || (term.IsConstantArray && IsValue(term.Arg(0)));

    /// <summary>The logic the assertions need, named from the sorts and declarations in them.</summary>
    private static string LogicOf(string body)
    {
        bool functions = body.Contains("(declare-sort ", StringComparison.Ordinal) || Function.IsMatch(body);
        string theories = (body.Contains("(Array ", StringComparison.Ordinal) ? "A" : string.Empty)
            + (functions ? "UF" : string.Empty)
            + (body.Contains("(_ BitVec ", StringComparison.Ordinal) ? "BV" : string.Empty)
            + (body.Contains("(declare-datatypes ", StringComparison.Ordinal) ? "DT" : string.Empty)
            + (body.Contains("(Seq ", StringComparison.Ordinal) ? "S" : string.Empty)
            + (Integer.IsMatch(body) ? "LIA" : string.Empty);
        return "QF_" + (theories.Length == 0 ? "UF" : theories);
    }

    [GeneratedRegex(@"[ (]Int[ )]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex Integer { get; }

    [GeneratedRegex(@"^\(declare-fun\s+(?<name>\|[^|]*\||\S+)\s+\(\)\s+(?<sort>Bool|\(_ BitVec \d+\))\)\s*$", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex ScalarConstant { get; }

    [GeneratedRegex(@"^\(declare-fun\s+(\|[^|]*\||\S+)\s+\([^)]", RegexOptions.Multiline | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 60_000)]
    private static partial Regex Function { get; }

    /// <summary>A printed query: its script, and the Bool and bit-vector constants its <c>get-value</c> asks for, each with its sort as printed.</summary>
    internal sealed record Printed(string Script, ImmutableArray<(string Name, string Sort)> Scalars)
    {
        private static readonly SearchValues<char> Hexadecimal = SearchValues.Create("0123456789abcdefABCDEF");

        /// <summary>
        /// <paramref name="values"/> as assertions Z3 parses, one constant at a time and by name: each constant declared as
        /// the script declares it and equal to its value. Null unless every constant asked for has a value of its own sort,
        /// written as a literal (<c>true</c>, <c>false</c>, <c>#b…</c> or <c>#x…</c> of the constant's width). A name is
        /// the same symbol with or without its <c>|...|</c>, and solvers differ in when they print them: cvc5 answers
        /// <c>|in.a|</c> as <c>in.a</c>.
        /// </summary>
        public string? Pins(ImmutableDictionary<string, string> values)
        {
            Dictionary<string, string> bySymbol = new(StringComparer.Ordinal);
            foreach ((string name, string value) in values)
            {
                bySymbol[name.Trim('|')] = value;
            }

            StringBuilder pins = new();
            foreach ((string name, string sort) in Scalars)
            {
                if (!bySymbol.TryGetValue(name.Trim('|'), out string? value) || !Fits(sort, value))
                {
                    return null;
                }

                pins.Append("(declare-fun ").Append(name).Append(" () ").Append(sort).Append(")\n(assert (= ").Append(name).Append(' ').Append(value).Append("))\n");
            }

            return pins.ToString();
        }

        private static bool Fits(string sort, string value)
        {
            if (string.Equals(sort, "Bool", StringComparison.Ordinal))
            {
                return value is "true" or "false";
            }

            int width = int.Parse(sort.AsSpan("(_ BitVec ".Length, sort.Length - "(_ BitVec )".Length), CultureInfo.InvariantCulture);
            ReadOnlySpan<char> digits = value.AsSpan(Math.Min(2, value.Length));
            return (value.StartsWith("#b", StringComparison.Ordinal) && digits.Length == width && !digits.ContainsAnyExcept('0', '1'))
                || (value.StartsWith("#x", StringComparison.Ordinal) && digits.Length * 4 == width && !digits.ContainsAnyExcept(Hexadecimal));
        }
    }

    /// <summary>
    /// One query and its answer: its status, and for a satisfiable one the model, Z3's own or the one Z3 completed from
    /// the second solver's values. It makes the queries it asks Z3, and disposes them.
    /// </summary>
    internal sealed class Asked(Context context, ProductEncoding encoding, VerificationOptions options, BoolExpr[] query) : IDisposable
    {
        private readonly SolverQuery asked = Z3Backend.Query(context, encoding, options, query);
        private SolverQuery? readBack;

        public Status Status { get; private set; } = Status.UNKNOWN;

        public SolverModel Model => (readBack ?? asked).Model;

        /// <summary>The detail of a query left a timeout: Z3's reason for giving up and the limit it hit.</summary>
        public string Timeout() => Z3Backend.Timeout(asked, options);

        /// <summary>Z3's own answer to the query, as the stage <paramref name="name"/>.</summary>
        public Status AskZ3(string name, long? interruptAfterMs) => Status = asked.Check(options, name, interruptAfterMs);

        /// <summary>The second solver answered unsat.</summary>
        public void Refuted() => Status = Status.UNSATISFIABLE;

        /// <summary>
        /// Asks Z3 the query again beside <paramref name="pins"/>, the second solver's values. Whether Z3 satisfies it:
        /// then the query is satisfiable and that model is its model. If not, it stays the unknown it was.
        /// </summary>
        public bool ReadBack(string name, BoolExpr[] pins, long? interruptAfterMs)
        {
            readBack = Z3Backend.Query(context, encoding, options, [.. query, .. pins]);
            bool satisfied = readBack.Check(options, name, interruptAfterMs) == Status.SATISFIABLE;
            Status = satisfied ? Status.SATISFIABLE : Status.UNKNOWN;
            return satisfied;
        }

        public void Dispose()
        {
            readBack?.Dispose();
            asked.Dispose();
        }
    }
}
