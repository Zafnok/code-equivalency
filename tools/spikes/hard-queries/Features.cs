using System.Globalization;

using Microsoft.Z3;

namespace HardQueriesSpike;

/// <summary>
/// What an assertion set is made of (P2-101 criterion 1): how many distinct terms and sorts, and which theories. Counted
/// over the terms as a graph, each distinct term once. Numbers only: no name from the analysed code is kept, except
/// whether some uninterpreted sort is the string type's.
/// </summary>
internal sealed class Features
{
    public const string Header =
        "terms\tsorts\tbvWidths\tarraySorts\tuninterpretedSorts\tstringSort\tdatatypeSorts\tsequenceSorts\tintSort\tfloatSorts"
        + "\tmul\tmulNonlinear\tdiv\tmulOverflow\totherOverflow\tshiftByTerm\tselect\tstore\tconstArray"
        + "\tcallApps\tcallFunctions\tpureApps\tpureFunctions\totherFunctionApps\tdatatypeOps\tsequenceOps\tintOps\tite\tdistinct\tquantifiers";

    private readonly Dictionary<string, string> sortKinds = new(StringComparer.Ordinal);
    private readonly SortedSet<uint> widths = [];
    private readonly HashSet<string> callFunctions = new(StringComparer.Ordinal);
    private readonly HashSet<string> pureFunctions = new(StringComparer.Ordinal);

    public int Terms { get; private set; }

    public bool StringSort { get; private set; }

    public int Mul { get; private set; }

    public int MulNonlinear { get; private set; }

    public int Div { get; private set; }

    public int MulOverflow { get; private set; }

    public int OtherOverflow { get; private set; }

    public int ShiftByTerm { get; private set; }

    public int Select { get; private set; }

    public int Store { get; private set; }

    public int ConstArray { get; private set; }

    public int CallApps { get; private set; }

    public int PureApps { get; private set; }

    public int OtherFunctionApps { get; private set; }

    public int DatatypeOps { get; private set; }

    public int SequenceOps { get; private set; }

    public int IntOps { get; private set; }

    public int Ite { get; private set; }

    public int Distinct { get; private set; }

    public int Quantifiers { get; private set; }

    public int Sorts => sortKinds.Count;

    public int SortsOf(string kind) => sortKinds.Values.Count(k => k == kind);

    /// <summary>The features of the terms reachable from <paramref name="roots"/>.</summary>
    public static Features Of(IEnumerable<Expr> roots)
    {
        Features features = new();
        HashSet<uint> seen = [];
        Stack<Expr> pending = new();
        foreach (Expr root in roots)
        {
            if (seen.Add(root.Id))
            {
                pending.Push(root);
            }
        }

        while (pending.TryPop(out Expr? term))
        {
            if (term.IsQuantifier)
            {
                features.Quantifiers++;
                Expr body = ((Quantifier)term).Body;
                if (seen.Add(body.Id))
                {
                    pending.Push(body);
                }

                continue;
            }

            if (!term.IsApp)
            {
                continue;
            }

            features.Terms++;
            features.Sort(term.Sort);
            Expr[] arguments = term.Args;
            features.Count(term, arguments);
            foreach (Expr argument in arguments)
            {
                if (seen.Add(argument.Id))
                {
                    pending.Push(argument);
                }
            }
        }

        return features;
    }

    /// <summary>How many distinct terms <paramref name="roots"/> reach, and nothing else.</summary>
    public static int Size(IEnumerable<Expr> roots) => Of(roots).Terms;

    public string Line() => string.Join('\t', new object[]
    {
        Terms, Sorts, string.Join(',', widths), SortsOf("array"), SortsOf("uninterpreted"), StringSort ? 1 : 0, SortsOf("datatype"), SortsOf("sequence"), SortsOf("int"), SortsOf("float"),
        Mul, MulNonlinear, Div, MulOverflow, OtherOverflow, ShiftByTerm, Select, Store, ConstArray,
        CallApps, callFunctions.Count, PureApps, pureFunctions.Count, OtherFunctionApps, DatatypeOps, SequenceOps, IntOps, Ite, Distinct, Quantifiers,
    }.Select(static v => Convert.ToString(v, CultureInfo.InvariantCulture)));

    private void Sort(Sort sort)
    {
        string name = sort.ToString();
        if (sortKinds.ContainsKey(name))
        {
            return;
        }

        string kind = sort.SortKind switch
        {
            Z3_sort_kind.Z3_BOOL_SORT => "bool",
            Z3_sort_kind.Z3_BV_SORT => "bitvector",
            Z3_sort_kind.Z3_ARRAY_SORT => "array",
            Z3_sort_kind.Z3_UNINTERPRETED_SORT => "uninterpreted",
            Z3_sort_kind.Z3_DATATYPE_SORT => "datatype",
            Z3_sort_kind.Z3_SEQ_SORT => "sequence",
            Z3_sort_kind.Z3_INT_SORT => "int",
            Z3_sort_kind.Z3_FLOATING_POINT_SORT or Z3_sort_kind.Z3_ROUNDING_MODE_SORT => "float",
            _ => "other",
        };
        sortKinds.Add(name, kind);
        if (sort is BitVecSort bits)
        {
            widths.Add(bits.Size);
        }

        StringSort |= kind == "uninterpreted" && name.Contains("string", StringComparison.OrdinalIgnoreCase);
    }

    private void Count(Expr term, Expr[] arguments)
    {
        FuncDecl function = term.FuncDecl;
        string kind = function.DeclKind.ToString();
        switch (kind)
        {
            case "Z3_OP_BMUL":
                Mul++;
                MulNonlinear += arguments.Count(static a => !a.IsNumeral) > 1 ? 1 : 0;
                break;
            case "Z3_OP_BSMUL_NO_OVFL" or "Z3_OP_BUMUL_NO_OVFL" or "Z3_OP_BSMUL_NO_UDFL":
                MulOverflow++;
                break;
            case "Z3_OP_BSHL" or "Z3_OP_BLSHR" or "Z3_OP_BASHR":
                ShiftByTerm += arguments[1].IsNumeral ? 0 : 1;
                break;
            case "Z3_OP_SELECT":
                Select++;
                break;
            case "Z3_OP_STORE":
                Store++;
                break;
            case "Z3_OP_CONST_ARRAY":
                ConstArray++;
                break;
            case "Z3_OP_ITE":
                Ite++;
                break;
            case "Z3_OP_DISTINCT":
                Distinct++;
                break;
            case "Z3_OP_UNINTERPRETED" when arguments.Length > 0:
                string name = function.Name.ToString();
                if (name.StartsWith("pure:", StringComparison.Ordinal) || name.StartsWith("pure.threw:", StringComparison.Ordinal))
                {
                    PureApps++;
                    pureFunctions.Add(name);
                }
                else if (name.StartsWith("f:", StringComparison.Ordinal) || name.StartsWith("threw:", StringComparison.Ordinal)
                    || name.StartsWith("heap:", StringComparison.Ordinal) || name.StartsWith("refout:", StringComparison.Ordinal))
                {
                    CallApps++;
                    callFunctions.Add(name);
                }
                else
                {
                    OtherFunctionApps++;
                }

                break;
            default:
                if (kind.Contains("DIV", StringComparison.Ordinal) || kind.Contains("REM", StringComparison.Ordinal) || kind.Contains("SMOD", StringComparison.Ordinal))
                {
                    Div += kind.StartsWith("Z3_OP_B", StringComparison.Ordinal) && !kind.Contains("NO_OVFL", StringComparison.Ordinal) ? 1 : 0;
                    OtherOverflow += kind.Contains("NO_OVFL", StringComparison.Ordinal) ? 1 : 0;
                }
                else if (kind.Contains("_NO_OVFL", StringComparison.Ordinal) || kind.Contains("_NO_UDFL", StringComparison.Ordinal)
                    || kind.Contains("_OVFL", StringComparison.Ordinal))
                {
                    OtherOverflow++;
                }
                else if (kind.StartsWith("Z3_OP_DT_", StringComparison.Ordinal))
                {
                    DatatypeOps++;
                }
                else if (kind.StartsWith("Z3_OP_SEQ_", StringComparison.Ordinal))
                {
                    SequenceOps++;
                }
                else if (term.Sort.SortKind == Z3_sort_kind.Z3_INT_SORT && arguments.Length > 0)
                {
                    IntOps++;
                }

                break;
        }
    }
}
