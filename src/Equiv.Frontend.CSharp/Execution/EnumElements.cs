using System.Globalization;

using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// An enum argument or field built from the sort element the model holds it as (ticket P2-251). The lowering maps an enum
/// to the sort of its metadata name and a constant of it to the element <see cref="TypeMapper.Constant(ITypeSymbol, object?)"/>
/// gives its value (VERIFICATION-MODEL section 2), so an element that is a member's constant element, or <c>0</c>'s, which
/// <c>default</c> lowers to, is that value. Any other element is a value no member has: the largest member plus one, or for a
/// <c>[Flags]</c> enum every member's bits together plus one, so that no combination of members is it either; the model's
/// other such elements of the sort, in id order, take the values after it, so equal elements are equal values and distinct
/// elements distinct ones. The members are both sides' (<c>counterpart</c> is the other side's compilation, when there is
/// one) and the elements are every input of the model, so the two sides build one value from one element.
/// </summary>
internal sealed class EnumElements(Counterexample model, IReadOnlyDictionary<string, IrValue> maps, Compilation? counterpart)
{
    private const string Flags = "System.FlagsAttribute";

    /// <summary><paramref name="element"/> as a wire value of <paramref name="type"/>; null when the value it is does not fit the underlying type.</summary>
    public string? Wire(INamedTypeSymbol type, IrSortValue element)
    {
        INamedTypeSymbol underlying = type.EnumUnderlyingType!;
        int width = ((IrBitVec)TypeMapper.Map(underlying)).Width;
        bool signed = TypeMapper.IsSigned(underlying);
        INamedTypeSymbol[] types = [type, .. Counterpart(type)];
        Dictionary<long, Int128> known = [];
        known.TryAdd(((IrSortValue)TypeMapper.Constant(type, 0)).Id, 0);
        foreach ((INamedTypeSymbol owner, object value) in types.SelectMany(static t => t.GetMembers().OfType<IFieldSymbol>().Where(static f => f.HasConstantValue).Select(f => (t, f.ConstantValue!))))
        {
            known.TryAdd(((IrSortValue)TypeMapper.Constant(owner, value)).Id, Number(owner, value));
        }

        if (known.TryGetValue(element.Id, out Int128 member))
        {
            return Text(member);
        }

        Int128 mask = (Int128.One << width) - 1;
        bool flags = Array.Exists(types, static t => t.GetAttributes().Any(static a => string.Equals(a.AttributeClass!.ToDisplayString(), Flags, StringComparison.Ordinal)));
        Int128 first = flags ? known.Values.Aggregate(Int128.Zero, (bits, v) => bits | (v & mask)) + 1 : known.Values.Max() + 1;
        Int128 last = signed && !flags ? mask >> 1 : mask;
        long[] fresh = [.. Ids(element.Sort).Where(id => !known.ContainsKey(id)).Append(element.Id).Distinct().Order()];
        Int128 chosen = first + Array.IndexOf(fresh, element.Id);
        Int128 number = signed && chosen > mask >> 1 ? chosen - mask - 1 : chosen;
        return chosen > last ? null : Text(number);
    }

    /// <summary>The other side's enum of <paramref name="type"/>'s metadata name, when it has one.</summary>
    private IEnumerable<INamedTypeSymbol> Counterpart(INamedTypeSymbol type) =>
        counterpart?.GetTypeByMetadataName(TypeMapper.MetadataName(type)) is { TypeKind: TypeKind.Enum } other ? [other] : [];

    /// <summary>The ids of every element of <paramref name="sort"/> the model's inputs hold, a map's keys, values and default among them.</summary>
    private IEnumerable<long> Ids(string sort) =>
        model.Inputs.Arguments.Concat(maps.Values).SelectMany(Elements).Where(e => string.Equals(e.Sort, sort, StringComparison.Ordinal)).Select(static e => e.Id);

    private static IEnumerable<IrSortValue> Elements(IrValue value) => value switch
    {
        IrSortValue element => [element],
        IrMapValue map => Elements(map.Default).Concat(map.Entries.SelectMany(static e => Elements(e.Key).Concat(Elements(e.Value)))),
        _ => [],
    };

    private static Int128 Number(INamedTypeSymbol type, object value) =>
        TypeMapper.IsSigned(type.EnumUnderlyingType!) ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : Convert.ToUInt64(value, CultureInfo.InvariantCulture);

    private static string Text(Int128 value) => value.ToString(CultureInfo.InvariantCulture);
}
