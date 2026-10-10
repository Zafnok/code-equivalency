namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// One item of a member entry's modern argument list (ADR 0020; ticket M3-009). With a <see cref="Source"/>, it is the
/// legacy call's source argument at that position: the receiver of an instance or extension call is argument 0, and a
/// <c>params</c> array's elements count one by one. <see cref="Unwrap"/> takes that argument without its outermost
/// implicit conversion; <see cref="ConvertTo"/> (a metadata name) passes it through the implicit boxing or reference
/// conversion to that type. Without a <see cref="Source"/>, it is the constant <see cref="Constant"/> (its invariant text,
/// or null for <c>null</c>) of the IR type <see cref="ConstantType"/>: <c>bool</c>, <c>bv</c><i>n</i>, or a sort name.
/// With <see cref="Rest"/> (ticket P2-143), <see cref="Source"/> is where the elements of the legacy call's <c>params</c>
/// array start, and the item is all of them, to the last source argument, as the <c>System.ReadOnlySpan&lt;T&gt;</c> the
/// compiler builds from the same elements for the modern member's <c>params</c> span parameter.
/// With <see cref="TypeArgument"/> (ticket P2-117), the item passes nothing: the legacy call's source argument at
/// <see cref="Source"/> is a <c>typeof</c> of an enum type, which is the modern member's type argument.
/// </summary>
public sealed record ApiArgument(int? Source, bool Unwrap = false, string? ConvertTo = null, string? ConstantType = null, string? Constant = null, bool Rest = false)
{
    /// <summary>
    /// With a <see cref="Source"/>: the argument, as the item takes it, is an integer known at the call to lie in this
    /// range, and is passed as a signed integer of the range's width (ticket P2-142). Null for any other item.
    /// </summary>
    public ApiIntegerRange? Range { get; init; }

    /// <summary>
    /// The source argument is <c>typeof(E)</c> for an enum type <c>E</c>, and <c>E</c> is the type argument the modern
    /// member is constructed with: <see cref="ApiEquivalence.ModernOf"/> names it (ticket P2-117). A call whose argument
    /// is any other <c>System.Type</c> is left as it is.
    /// </summary>
    public bool TypeArgument { get; init; }

    /// <summary>
    /// With a <see cref="Source"/>: the argument, as the item takes it, is of exactly the type a
    /// <see cref="TypeArgument"/> item of the same entry names (ticket P2-117). A call where it is not is left as it is.
    /// </summary>
    public bool OfTypeArgument { get; init; }
}
