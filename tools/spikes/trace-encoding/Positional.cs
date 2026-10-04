using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;
using Side = Equiv.Verify.Z3.ProductEncoder.Side;

namespace TraceEncodingSpike;

/// <summary>
/// One pair's product twice: as <see cref="ProductEncoder.Encode"/> builds it, and with "the traces are equal" written
/// without sequences. <c>OldSites</c> and <c>NewSites</c> count the call sites, <c>Pairs</c> the pairs of one site a side
/// that can stand at the same position, each of which is one conjunct.
/// </summary>
internal sealed record Product(ProductEncoding Sequence, ProductEncoding Positional, int OldSites, int NewSites, int Pairs);

/// <summary>
/// The positional trace (ticket P1-034). The product is <see cref="ProductEncoder.Encode"/>'s own: its assertions are
/// used as they are. Only <see cref="ProductEncoding.Differs"/> is rebuilt, from two <see cref="FragmentEncoder"/>s made
/// over the encoding's own sorts and call encoders, which name every term as the first two did and so give the same
/// terms, and add what the encoding does not hand out: each side's call sites and exits. Two of its conjuncts change.
/// The traces are compared by position, and the exception types, an integer in production, are compared as bit-vectors,
/// so that nothing in the query is a sequence, a datatype or an integer.
/// </summary>
internal static class Positional
{
    public static Product Build(Context context, IrProcedure old, IrProcedure @new, ImmutableDictionary<string, string> callIdentityMap)
    {
        ProductEncoding sequence = ProductEncoder.Encode(context, old, @new, callIdentityMap);
        TraceEncoder calls = sequence.Calls;
        ImmutableArray<Expr> heapInputs = [.. calls.Heap.Select(m => sequence.Inputs.First(i => string.Equals(i.Shared.Var.Name, m.Name, StringComparison.Ordinal) && i.Shared.Type == m.Type).Term)];
        Dictionary<string, int> exceptionTypes = new(StringComparer.Ordinal);
        FragmentEncoder oldSide = new(Side.Old, old, sequence.Sorts, (calls, sequence.Pures), Bound(sequence.Inputs, static s => s.Old), heapInputs, exceptionTypes);
        FragmentEncoder newSide = new(Side.New, @new, sequence.Sorts, (calls, sequence.Pures), Bound(sequence.Inputs, static s => s.New), heapInputs, exceptionTypes);

        // The rebuild is the production term when given the production conjuncts, or the spike measures something else.
        BoolExpr rebuilt = Differs(context, sequence, oldSide, newSide, context.MkEq(oldSide.ExceptionType, newSide.ExceptionType), context.MkEq(oldSide.Trace!, newSide.Trace!));
        if (!rebuilt.Equals(sequence.Differs))
        {
            throw new InvalidOperationException($"{old.Identity.Value}: the rebuilt query is not ProductEncoder's.");
        }

        Dictionary<string, int> ids = new(StringComparer.Ordinal);
        BoolExpr exceptions = context.MkEq(ExceptionType(context, oldSide, ids), ExceptionType(context, newSide, ids));
        (List<Site> oldSites, BitVecExpr oldLength) = Sites(context, calls, Side.Old, oldSide);
        (List<Site> newSites, BitVecExpr newLength) = Sites(context, calls, Side.New, newSide);
        (BoolExpr traces, int pairs) = TracesEqual(context, oldSites, oldLength, newSites, newLength);
        return new Product(sequence, sequence with { Differs = Differs(context, sequence, oldSide, newSide, exceptions, traces) }, oldSites.Count, newSites.Count, pairs);
    }

    /// <summary>
    /// The traces are equal: they are as long, and any two calls, one a side, that are both made and stand at the same
    /// position are the same event. Two calls of different callees or signatures never are; two of the same are when
    /// their arguments, and the heap they read, are equal. A pair whose positions cannot meet on any path through the two
    /// control-flow graphs says nothing and is left out.
    /// </summary>
    private static (BoolExpr Equal, int Pairs) TracesEqual(Context context, List<Site> old, BitVecExpr oldLength, List<Site> @new, BitVecExpr newLength)
    {
        List<BoolExpr> conjuncts = [context.MkEq(oldLength, newLength)];
        int pairs = 0;
        foreach (Site a in old)
        {
            foreach (Site b in @new)
            {
                if (a.Max < b.Min || b.Max < a.Min)
                {
                    continue;
                }

                pairs++;
                bool fixedPosition = a.Min == a.Max && b.Min == b.Max;
                BoolExpr meet = fixedPosition ? context.MkAnd(a.Reach, b.Reach) : context.MkAnd(a.Reach, b.Reach, context.MkEq(a.Position, b.Position));
                if (!string.Equals(a.Shape, b.Shape, StringComparison.Ordinal))
                {
                    conjuncts.Add(context.MkNot(meet));
                    continue;
                }

                BoolExpr[] equalities = [.. a.Values.Zip(b.Values).Where(static v => !v.First.Equals(v.Second)).Select(v => context.MkEq(v.First, v.Second))];
                if (equalities.Length > 0)
                {
                    conjuncts.Add(context.MkImplies(meet, equalities.Length == 1 ? equalities[0] : context.MkAnd(equalities)));
                }
            }
        }

        return (conjuncts.Count == 1 ? conjuncts[0] : context.MkAnd(conjuncts), pairs);
    }

    /// <summary>
    /// One side's call sites in trace order, and the length of its trace. A site's position is the encoding's own
    /// <c>cnt</c> term: the number of calls made before it on the path taken. <c>Min</c> and <c>Max</c> bound it over every
    /// path of the control-flow graph. The length is the count at the block the path ends in.
    /// </summary>
    private static (List<Site> Sites, BitVecExpr Length) Sites(Context context, TraceEncoder calls, Side side, FragmentEncoder encoder)
    {
        IrProcedure procedure = encoder.Procedure;
        Dictionary<IrBlockId, (int Min, int Max)> before = new() { [procedure.Entry] = (0, 0) };
        List<Site> sites = [];
        List<(BoolExpr Reach, BitVecExpr Count)> ends = [];
        foreach (IrBlock block in IrLoopAnalysis.Of(procedure).ReversePostorder)
        {
            // A block no edge enters is never reached; any range is right for it.
            (int min, int max) = before.GetValueOrDefault(block.Id, (0, int.MaxValue / 2));
            int made = 0;
            foreach (IrCall call in block.Instructions.OfType<IrCall>())
            {
                FragmentEncoder.CallSite site = encoder.CallSites[sites.Count];
                if (!ReferenceEquals(site.Call, call))
                {
                    throw new InvalidOperationException($"{procedure.Identity.Value}: the call sites are not in trace order.");
                }

                // What TraceEncoder.Call boxes into the event: the arguments, then the heap read unless the callee is closed.
                IEnumerable<(IrType Type, Expr Term)> heap = calls.IsClosed(side, call.Callee) ? [] : calls.Heap.Zip(site.HeapIn, static (m, term) => (m.Type, term));
                (IrType Type, Expr Term)[] read = [.. site.Args, .. heap];
                string shape = calls.Canonical(side, call.Callee) + "\u0001" + string.Join('\u0001', read.Select(static r => SortMapper.Name(r.Type)));
                sites.Add(new Site(site.Reach, site.Position, shape, [.. read.Select(static r => r.Term)], min + made, max + made));
                made++;
            }

            foreach (IrBlockId target in Successors(block.Terminator))
            {
                before[target] = before.TryGetValue(target, out (int Min, int Max) seen)
                    ? (Math.Min(seen.Min, min + made), Math.Max(seen.Max, max + made))
                    : (min + made, max + made);
            }

            if (block.Terminator is IrReturn or IrThrow or IrUnreachable)
            {
                BitVecExpr count = context.MkBVConst($"{ProductEncoder.Prefix(side)}.cnt.B{block.Id.Value.ToString(CultureInfo.InvariantCulture)}", 32);
                ends.Add((encoder.Terms.Reach[block.Id], made == 0 ? count : context.MkBVAdd(count, context.MkBV(made, 32))));
            }
        }

        BitVecExpr length = ends.Count == 0
            ? context.MkBV(0, 32)
            : ends.Take(ends.Count - 1).Reverse().Aggregate(ends[^1].Count, (rest, e) => (BitVecExpr)context.MkITE(e.Reach, e.Count, rest));
        return (sites, length);
    }

    private static IEnumerable<IrBlockId> Successors(IrTerminator terminator) => terminator switch
    {
        IrGoto jump => [jump.Target],
        IrBranch branch => [branch.Then, branch.Else],
        IrSwitch choice => [.. choice.Cases.Select(static c => c.Target), choice.Default],
        _ => [],
    };

    /// <summary>The exception type thrown as a bit-vector, 0 when no throw is reached: <see cref="FragmentEncoder.ExceptionType"/> without integers.</summary>
    private static BitVecExpr ExceptionType(Context context, FragmentEncoder side, Dictionary<string, int> ids) =>
        side.Exits
            .Where(static e => e.Exit is IrThrow)
            .Reverse()
            .Aggregate((BitVecExpr)context.MkBV(0, 32), (rest, e) =>
            {
                string type = ((IrThrow)e.Exit).ExceptionType;
                if (!ids.TryGetValue(type, out int id))
                {
                    id = ids.Count + 1;
                    ids.Add(type, id);
                }

                return (BitVecExpr)context.MkITE(side.Terms.Reach[e.Block], context.MkBV(id, 32), rest);
            });

    /// <summary>"Some observable differs", as <see cref="ProductEncoder.Encode"/> builds it, with the two conjuncts given.</summary>
    private static BoolExpr Differs(Context context, ProductEncoding encoding, FragmentEncoder oldSide, FragmentEncoder newSide, BoolExpr exceptionTypes, BoolExpr traces)
    {
        List<BoolExpr> equal =
        [
            context.MkEq(oldSide.Returned, newSide.Returned),
            ReturnsEqual(context, encoding.Sorts, oldSide, newSide),
            context.MkEq(oldSide.Threw, newSide.Threw),
            exceptionTypes,
        ];
        equal.AddRange(encoding.Inputs
            .Where(static i => i.Shared.ByRef)
            .Select(i => context.MkEq(oldSide.Final(i.Shared.Old, i.Shared.Var, i.Term), newSide.Final(i.Shared.New, i.Shared.Var, i.Term))));
        equal.Add(traces);
        return context.MkNot(context.MkAnd(equal));
    }

    /// <summary><c>ProductEncoder.ReturnsEqual</c>, which is private.</summary>
    private static BoolExpr ReturnsEqual(Context context, SortMapper sorts, FragmentEncoder old, FragmentEncoder @new)
    {
        IrType? oldType = old.Procedure.ReturnType;
        if (oldType != @new.Procedure.ReturnType)
        {
            return context.MkNot(context.MkOr(old.Returned, @new.Returned));
        }

        if (oldType is null)
        {
            return context.MkTrue();
        }

        Expr none = context.MkConst("ret.none", sorts.Sort(oldType));
        return context.MkEq(old.ReturnValue(none), @new.ReturnValue(none));
    }

    /// <summary><c>ProductEncoder.Bound</c>, which is private: one side's parameter names and the input each is bound to.</summary>
    private static Dictionary<string, Expr> Bound(ImmutableArray<(SharedParameter Shared, Expr Term)> inputs, Func<SharedParameter, IrParameter?> side) =>
        inputs
            .Where(i => side(i.Shared) is not null)
            .ToDictionary(i => side(i.Shared)!.Var.Name, static i => i.Term, StringComparer.Ordinal);

    private sealed record Site(BoolExpr Reach, BitVecExpr Position, string Shape, ImmutableArray<Expr> Values, int Min, int Max);
}

/// <summary>What Z3 prints that SMT-LIB does not allow, written as SMT-LIB has it.</summary>
internal static class Smt
{
    private static readonly string[] Heads = ["seq.++", "or", "and"];

    /// <summary>
    /// <paramref name="script"/> with every <c>seq.++</c>, <c>or</c> and <c>and</c> of one argument written as that
    /// argument. Z3 prints all three; cvc5 rejects the first and Bitwuzla the other two. P1-025's
    /// <c>SmtFile.Unwrapped</c> with more than one head.
    /// </summary>
    public static string Unwrap(string script)
    {
        StringBuilder output = new(script.Length);

        // One frame per open parenthesis: where it starts in the output, the head if it is one of Heads, and its arguments so far.
        Stack<(int Start, string? Head, int Arguments)> open = new();
        bool inAtom = false;
        for (int i = 0; i < script.Length; i++)
        {
            char c = script[i];
            if (c == '(')
            {
                Argument(open);
                string? head = Heads.FirstOrDefault(h => string.CompareOrdinal(script, i + 1, h, 0, h.Length) == 0 && char.IsWhiteSpace(script[i + 1 + h.Length]));
                open.Push((output.Length, head, -1));
                output.Append(c);
                inAtom = false;
            }
            else if (c == ')')
            {
                (int start, string? head, int arguments) = open.Pop();
                inAtom = false;
                if (head is not null && arguments == 1)
                {
                    string argument = output.ToString(start + 1 + head.Length, output.Length - start - 1 - head.Length).Trim();
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
        static void Argument(Stack<(int Start, string? Head, int Arguments)> open)
        {
            if (open.TryPop(out (int Start, string? Head, int Arguments) frame))
            {
                open.Push(frame with { Arguments = frame.Arguments + 1 });
            }
        }
    }
}
