using System.Collections.Immutable;

using Equiv.Core.Ir;

using Microsoft.Z3;

namespace Equiv.Verify.Z3;

/// <summary>
/// IR types to Z3 sorts and IR literals to Z3 terms, for one <see cref="Context"/> (VERIFICATION-MODEL.md
/// section 2; ticket M3-001). <see cref="IrBool"/> is <c>Bool</c>, <see cref="IrBitVec"/> a bitvector,
/// <see cref="IrMap"/> an array, and each <see cref="IrSort"/> name one uninterpreted sort that both sides
/// share. A sort literal <c>sort "S" n</c> is the constant <c>lit.S.n</c>; <see cref="Distinctness"/> keeps
/// literals of one sort apart, as the interpreter compares them by id.
/// </summary>
internal sealed class SortMapper(Context context)
{
    private readonly Dictionary<string, UninterpretedSort> uninterpreted = new(StringComparer.Ordinal);
    private readonly Dictionary<IrSortValue, Expr> literals = [];

    public Context Context => context;

    /// <summary>Every sort literal created so far, for the model decoder to name the elements they denote.</summary>
    public IReadOnlyDictionary<IrSortValue, Expr> SortLiterals => literals;

    /// <summary>A stable, human-readable name for <paramref name="type"/>, used in Z3 symbol names.</summary>
    public static string Name(IrType type) => type switch
    {
        IrBool => "bool",
        IrBitVec bitVec => "bv" + bitVec.Width.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IrSort sort => $"sort<{sort.Name}>",
        _ => $"map<{Name(((IrMap)type).Key)},{Name(((IrMap)type).Value)}>",
    };

    public Sort Sort(IrType type) => type switch
    {
        IrBool => context.BoolSort,
        IrBitVec bitVec => context.MkBitVecSort((uint)bitVec.Width),
        IrSort sort => Uninterpreted(sort.Name),
        _ => context.MkArraySort(Sort(((IrMap)type).Key), Sort(((IrMap)type).Value)),
    };

    public Expr Literal(IrValue value) => value switch
    {
        IrBoolValue boolean => context.MkBool(boolean.Value),
        IrBitVecValue bits => context.MkBV(bits.Bits, (uint)bits.Width),
        IrSortValue element => SortLiteral(element),
        _ => MapLiteral((IrMapValue)value),
    };

    /// <summary>One <c>distinct</c> per sort with at least two literals.</summary>
    public ImmutableArray<BoolExpr> Distinctness() =>
    [
        .. literals
            .GroupBy(static l => l.Key.Sort, StringComparer.Ordinal)
            .Where(static g => g.Skip(1).Any())
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .Select(g => context.MkDistinct([.. g.OrderBy(static l => l.Key.Id).Select(static l => l.Value)])),
    ];

    private UninterpretedSort Uninterpreted(string name)
    {
        if (!uninterpreted.TryGetValue(name, out UninterpretedSort? sort))
        {
            sort = context.MkUninterpretedSort(name);
            uninterpreted.Add(name, sort);
        }

        return sort;
    }

    private Expr SortLiteral(IrSortValue element)
    {
        if (!literals.TryGetValue(element, out Expr? constant))
        {
            constant = context.MkConst($"lit.{element.Sort}.{element.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}", Uninterpreted(element.Sort));
            literals.Add(element, constant);
        }

        return constant;
    }

    private ArrayExpr MapLiteral(IrMapValue map)
    {
        ArrayExpr array = context.MkConstArray(Sort(map.MapType.Key), Literal(map.Default));
        foreach (KeyValuePair<IrValue, IrValue> entry in map.Entries.OrderBy(static e => e.Key.ToString(), StringComparer.Ordinal))
        {
            array = context.MkStore(array, Literal(entry.Key), Literal(entry.Value));
        }

        return array;
    }
}
