using System.Globalization;
using System.Text;

using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// What one replay or <c>probe</c> driver's generated source holds besides its call (ADR 0062; ticket P2-245): the types its
/// wire names by index, as <c>typeof</c> where the friend assembly can name them and by metadata name where it cannot, and
/// its makers, each a source expression the wire calls by index with its own wire arguments (a public constructor, a
/// delegate). The wire forms it writes are JSON arrays that the driver's <c>V.Value</c> reads against the type it is
/// building: <c>["@u",id,type,[[owner,field,value],...]]</c> an instance created without a constructor whose fields are
/// then written, <c>["@s",type,fields]</c> the same for a struct, <c>["@a",id,element,[values]]</c> an array,
/// <c>["@c",id,maker,[arguments]]</c> what a maker returns, <c>["@r",id]</c> the instance built earlier under that id, and
/// <c>["@t",cancelled]</c> a <c>CancellationToken</c>. One id is one instance in one case, so a model element reached twice
/// is one object, and a cycle closes because an instance is registered before its fields are written.
/// </summary>
internal sealed class DriverHeap
{
    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly List<ITypeSymbol> types = [];

    private readonly List<string> makers = [];

    private int ids;

    /// <summary>The types the wire names, by index.</summary>
    public IReadOnlyList<ITypeSymbol> Types => types;

    /// <summary>Each maker's source: an expression of the wire arguments <c>w</c> that builds one value.</summary>
    public IReadOnlyList<string> Makers => makers;

    /// <summary><paramref name="type"/>'s index in the driver's type table, added on first use.</summary>
    public int Type(ITypeSymbol type)
    {
        int index = types.FindIndex(t => SymbolEqualityComparer.Default.Equals(t, type));
        if (index >= 0)
        {
            return index;
        }

        types.Add(type);
        return types.Count - 1;
    }

    /// <summary>A new maker; returns its index.</summary>
    public int Maker(string source)
    {
        makers.Add(source);
        return makers.Count - 1;
    }

    /// <summary>A fresh instance id for this case.</summary>
    public string Id() => (++ids).ToString(CultureInfo.InvariantCulture);

    /// <summary>An instance of <paramref name="type"/> created without a constructor, then given <paramref name="fields"/>.</summary>
    public string Uninitialized(string id, ITypeSymbol type, IEnumerable<string> fields) =>
        $"[\"@u\",\"{id}\",{Index(type)},[{string.Join(',', fields)}]]";

    /// <summary>A struct of <paramref name="type"/> created without a constructor, then given <paramref name="fields"/>.</summary>
    public string Struct(ITypeSymbol type, IEnumerable<string> fields) => $"[\"@s\",{Index(type)},[{string.Join(',', fields)}]]";

    /// <summary>One field write: the type that declares <paramref name="field"/>, its runtime name, and its wire value.</summary>
    public string Field(IFieldSymbol field, string value) => $"[{Index(field.ContainingType)},{Text(field.Name)},{value}]";

    /// <summary>An array of <paramref name="element"/> holding <paramref name="values"/>.</summary>
    public string Array(string id, ITypeSymbol element, IEnumerable<string> values) => $"[\"@a\",\"{id}\",{Index(element)},[{string.Join(',', values)}]]";

    /// <summary>What maker <paramref name="maker"/> returns for <paramref name="arguments"/>, kept as instance <paramref name="id"/>.</summary>
    public static string Made(string id, int maker, IEnumerable<string> arguments) =>
        $"[\"@c\",\"{id}\",{maker.ToString(CultureInfo.InvariantCulture)},[{string.Join(',', arguments)}]]";

    /// <summary>The instance built earlier as <paramref name="id"/>.</summary>
    public static string Reference(string id) => $"[\"@r\",\"{id}\"]";

    /// <summary>A <c>CancellationToken</c>: cancelled, or <c>default</c>.</summary>
    public static string Token(bool cancelled) => cancelled ? "[\"@t\",true]" : "[\"@t\",false]";

    private string Index(ITypeSymbol type) => Type(type).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A maker that calls <paramref name="constructor"/> with its wire arguments, each read against its parameter's type: in
    /// source where a friend can see it, else bound by its exact signature and invoked as a hidden member is (ADR 0062
    /// decision 1). A constructor whose signature names a type no driver can name is not a maker (null).
    /// </summary>
    public int? Constructor(IMethodSymbol constructor)
    {
        string?[] parameters = [.. constructor.Parameters.Select(static p => TypeOf(p.Type))];
        string? owner = TypeOf(constructor.ContainingType);
        if (owner is null || parameters.Any(static t => t is null))
        {
            return null;
        }

        IEnumerable<string> values = parameters.Select(static (t, i) => $"V.Value(w[{i.ToString(CultureInfo.InvariantCulture)}], {t})");
        return Maker(Nameable(constructor.ContainingType) && ReplayArguments.Hidden(constructor) is null
            ? $"new {constructor.ContainingType.ToDisplayString(Qualified)}({string.Join(", ", constructor.Parameters.Zip(values, static (p, v) => $"({p.Type.ToDisplayString(Qualified)}){v}"))})"
            : $"H.Call(H.Ctor({owner}, new System.Type[] {{ {string.Join(", ", parameters)} }}), null, new object[] {{ {string.Join(", ", values)} }})");
    }

    /// <summary>
    /// A maker for a delegate of <paramref name="type"/> that answers every invocation with its wire argument 0 read against
    /// the return type, or, with <paramref name="answers"/> false, throws <c>InvalidOperationException</c>: a replay builds
    /// a delegate only where the model's runs never call it. Null when the type cannot be named or takes a parameter by
    /// reference.
    /// </summary>
    public int? Delegate(INamedTypeSymbol type, bool answers)
    {
        IMethodSymbol invoke = type.DelegateInvokeMethod!;
        if (!Nameable(type) || invoke.Parameters.Select(static p => p.RefKind).Append(invoke.RefKind).Any(static k => k != RefKind.None))
        {
            return null;
        }

        string parameters = string.Join(", ", invoke.Parameters.Select(static (_, i) => $"x{i.ToString(CultureInfo.InvariantCulture)}"));
        string returns = invoke.ReturnType.ToDisplayString(Qualified);
        string body = (answers, invoke.ReturnsVoid) switch
        {
            (false, _) => "{ throw new InvalidOperationException(\"the model's runs never call this delegate\"); }",
            (true, true) => "{ }",
            _ => $"({returns})V.Value(c, typeof({returns}))",
        };
        return Maker($"Answering(w, c => new {type.ToDisplayString(Qualified)}(({parameters}) => {body}))");
    }

    /// <summary>
    /// The C# expression that is <paramref name="type"/>'s <c>System.Type</c> in a driver: <c>typeof</c> where the friend
    /// assembly can name it, else its metadata name in its assembly, or a one-dimensional array of such a type; null for a
    /// type no driver names (a pointer, a type parameter, a generic or multi-dimensional array over a hidden type).
    /// </summary>
    public static string? TypeOf(ITypeSymbol type) => type switch
    {
        _ when Nameable(type) => $"typeof({type.ToDisplayString(Qualified)})",
        IArrayTypeSymbol { IsSZArray: true } array => TypeOf(array.ElementType) is { } element ? $"{element}.MakeArrayType()" : null,
        INamedTypeSymbol { IsGenericType: false } named => $"System.Type.GetType({Literal($"{TypeMapper.MetadataName(named)}, {named.ContainingAssembly.Identity.GetDisplayName()}")}, true)",
        _ => null,
    };

    /// <summary>Whether generated source can name <paramref name="type"/>: every type it is built from is visible to a friend.</summary>
    public static bool Nameable(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => Nameable(array.ElementType),
        INamedTypeSymbol named => ReplayArguments.Hidden(named) is null && named.TypeArguments.All(Nameable),
        _ => type is IDynamicTypeSymbol,
    };

    /// <summary><paramref name="value"/> as a C# string literal.</summary>
    public static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>
    /// A string as the driver's own minimal JSON text: only <c>\"</c>, <c>\\</c> and <c>\uXXXX</c> are escapes, so the
    /// driver's hand-written parser (which does not decode <c>\n</c>-style escapes) reads it back unchanged.
    /// </summary>
    public static string Text(string value)
    {
        StringBuilder text = new("\"");
        foreach (char c in value)
        {
            if (c is '"' or '\\')
            {
                text.Append('\\').Append(c);
            }
            else if (c is < ' ' or > '~')
            {
                text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.Append('"').ToString();
    }
}
