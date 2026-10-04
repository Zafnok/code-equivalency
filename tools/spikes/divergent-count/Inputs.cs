using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Verify.Z3;

using Microsoft.Z3;

using ProductEncoding = Equiv.Verify.Z3.ProductEncoder.ProductEncoding;
using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace DivergentCount;

/// <summary>
/// Criterion 1: whether a pair's source parameters span a space that can be counted. A source parameter is a shared
/// input that is not synthesised (<see cref="IrParameterNames.IsSynthesised"/>). It is countable when it is
/// <c>Bool</c> (one bit), a bitvector (its width), or a <c>Sort</c> that each body uses only as the key of a read of
/// its <c>null.*</c> map (one bit, the shadow; none when no body reads it). Any other use of a <c>Sort</c> parameter
/// is the pair's blocker, named by the first one in parameter order, legacy body first, then block order.
/// </summary>
internal static class Inputs
{
    /// <summary>A <c>Sort</c> parameter read or written through a map that is not its null shadow, cast or type test.</summary>
    public const string HeapMap = "heap-map";

    /// <summary>A <c>Sort</c> parameter whose value an operator, pure function, cast, type test, merge or exit reads.</summary>
    public const string SortByValue = "sort-by-value";

    /// <summary>A <c>Sort</c> parameter passed to a call or read by an opaque fragment, whose result depends on it.</summary>
    public const string CallResult = "call-result";

    private const string NullPrefix = "null.";

    public static Classification Classify(IrProcedure old, IrProcedure @new)
    {
        List<Source> sources = [];
        foreach (SharedParameter shared in ProductEncoder.Pair(old, @new).Where(static s => !IrParameterNames.IsSynthesised(s.Var.Name)))
        {
            switch (shared.Type)
            {
                case IrBool:
                    sources.Add(new Source(shared, 1, NullMap: null));
                    break;
                case IrBitVec bits:
                    sources.Add(new Source(shared, bits.Width, NullMap: null));
                    break;
                default:
                    (string? blocker, string? nullMap) = Uses(old, shared.Old).Concat(Uses(@new, shared.New)).Aggregate(
                        (Blocker: (string?)null, NullMap: (string?)null),
                        static (seen, use) => (seen.Blocker ?? use.Blocker, seen.NullMap ?? use.NullMap));
                    if (blocker is not null)
                    {
                        return new Classification([], blocker);
                    }

                    sources.Add(new Source(shared, nullMap is null ? 0 : 1, nullMap));
                    break;
            }
        }

        return new Classification([.. sources], Blocker: null);
    }

    /// <summary>The Bool terms the count projects on: every bit of every source parameter, in parameter order.</summary>
    public static BoolExpr[] Bits(Context context, ProductEncoding encoding, Classification classification)
    {
        List<BoolExpr> bits = [];
        foreach (Source source in classification.Sources)
        {
            Expr term = encoding.Inputs.First(i => i.Shared == source.Shared).Term;
            switch (source.Shared.Type)
            {
                case IrBool:
                    bits.Add((BoolExpr)term);
                    break;
                case IrBitVec bitVec:
                    BitVecExpr one = context.MkBV(1, 1);
                    bits.AddRange(Enumerable.Range(0, bitVec.Width).Select(i => context.MkEq(context.MkExtract((uint)i, (uint)i, (BitVecExpr)term), one)));
                    break;
                default:
                    if (source.NullMap is { } name)
                    {
                        Expr map = encoding.Inputs.First(i => string.Equals(i.Shared.Var.Name, name, StringComparison.Ordinal) && i.Shared.Type is IrMap { Value: IrBool }).Term;
                        bits.Add((BoolExpr)context.MkSelect((ArrayExpr)map, term));
                    }

                    break;
            }
        }

        return [.. bits];
    }

    /// <summary>Each use of <paramref name="parameter"/> in <paramref name="body"/>: its blocker, or the null map that reads it.</summary>
    private static IEnumerable<(string? Blocker, string? NullMap)> Uses(IrProcedure body, IrParameter? parameter)
    {
        if (parameter is null)
        {
            yield break;
        }

        IrVar p = parameter.Var;
        foreach (IrBlock block in body.Blocks)
        {
            foreach (IrInstruction instruction in block.Instructions)
            {
                switch (instruction)
                {
                    case IrMapRead read when read.Key == p && read.Map.Name.StartsWith(NullPrefix, StringComparison.Ordinal):
                        yield return (null, read.Map.Name);
                        break;
                    case IrMapRead read when read.Key == p || read.Map == p:
                        yield return (MapBlocker(read.Map), null);
                        break;
                    case IrMapWrite write when write.Key == p || write.Value == p || write.Map == p:
                        yield return (MapBlocker(write.Map), null);
                        break;
                    case IrCall call when call.Args.Contains(p) || call.Heap.Any(h => h.Before == p):
                        yield return (CallResult, null);
                        break;
                    case IrOpaque opaque when opaque.Reads.Contains(p) || opaque.Heap.Any(h => h.Before == p):
                        yield return (CallResult, null);
                        break;
                    case IrBinary binary when binary.A == p || binary.B == p:
                    case IrOverflows overflows when overflows.A == p || overflows.B == p:
                    case IrUnary unary when unary.A == p:
                    case IrPure pure when pure.Args.Contains(p):
                    case IrPhi phi when phi.Incoming.Any(i => i.Value == p):
                        yield return (SortByValue, null);
                        break;
                }
            }

            bool exits = block.Terminator switch
            {
                IrReturn ret => ret.Value == p || ret.Outs.Any(o => o.Final == p),
                IrThrow thrown => thrown.Outs.Any(o => o.Final == p),
                IrBranch branch => branch.Cond == p,
                IrSwitch @switch => @switch.Scrutinee == p,
                _ => false,
            };
            if (exits)
            {
                yield return (SortByValue, null);
            }
        }
    }

    /// <summary>A cast or type-test map is a function of the value; every other map is a heap slice.</summary>
    private static string MapBlocker(IrVar map) =>
        map.Name.StartsWith("cast.", StringComparison.Ordinal) || map.Name.StartsWith("istype.", StringComparison.Ordinal) ? SortByValue : HeapMap;
}

/// <summary>One countable source parameter: its bit count, and the null map whose read is its one bit when it is a reference.</summary>
internal sealed record Source(SharedParameter Shared, int Width, string? NullMap);

/// <summary>A pair's source parameters when all are countable, else the first blocker.</summary>
internal sealed record Classification(ImmutableArray<Source> Sources, string? Blocker)
{
    public bool Countable => Blocker is null;

    public int Bits => Sources.Sum(static s => s.Width);
}
