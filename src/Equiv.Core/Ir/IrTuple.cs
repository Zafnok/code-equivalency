using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// The one definition of how a value tuple is spelled in IR (VERIFICATION-MODEL.md section 2; ticket P2-027). A tuple of
/// <c>bool</c> and bitvector elements is the uninterpreted sort <c>tuple(&lt;element types&gt;)</c>, such as
/// <c>tuple(bv32,bool)</c>; a tuple literal is the pure function <see cref="New"/> of its elements, and an element read
/// the pure function <see cref="Item"/> of its position, whatever name the source gave it. A backend knows the element
/// types from the sort's name alone, which is what it needs to make the two functions each other's inverse.
/// </summary>
public static class IrTuple
{
    /// <summary>The pure function building a tuple from its elements, in order.</summary>
    public const string New = "tuple.new";

    private const string ItemPrefix = "tuple.item";

    private const string SortPrefix = "tuple(";

    /// <summary>The pure function reading element <paramref name="position"/> (1-based, as <c>Item1</c> is) of a tuple.</summary>
    public static string Item(int position) => ItemPrefix + position.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The 1-based element position <paramref name="function"/> reads, or null when it is not an element read.</summary>
    public static int? Position(string function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return function.StartsWith(ItemPrefix, StringComparison.Ordinal)
            && int.TryParse(function.AsSpan(ItemPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int position)
                ? position
                : null;
    }

    /// <summary>The sort of a tuple of <paramref name="elements"/>, each a <see cref="IrBool"/> or a <see cref="IrBitVec"/>.</summary>
    public static IrSort Sort(IEnumerable<IrType> elements) => new(SortPrefix + string.Join(',', elements.Select(IrText.Type)) + ")");

    /// <summary>The element types of a tuple sort, or empty when <paramref name="type"/> is not one.</summary>
    public static ImmutableArray<IrType> Elements(IrType type) =>
        type is IrSort { Name: var name } && name.StartsWith(SortPrefix, StringComparison.Ordinal) && name.EndsWith(')')
            ? [.. name[SortPrefix.Length..^1].Split(',').Select(Element)]
            : [];

    private static IrType Element(string text) =>
        string.Equals(text, "bool", StringComparison.Ordinal) ? new IrBool() : new IrBitVec(int.Parse(text.AsSpan(2), System.Globalization.CultureInfo.InvariantCulture));
}
