namespace Equiv.Core.ApiEquivalences;

/// <summary>
/// One item of a member entry's modern argument list (ADR 0020; ticket M3-009). With a <see cref="Source"/>, it is the
/// legacy call's source argument at that position: the receiver of an instance or extension call is argument 0, and a
/// <c>params</c> array's elements count one by one. <see cref="Unwrap"/> takes that argument without its outermost
/// implicit conversion; <see cref="ConvertTo"/> (a metadata name) passes it through the implicit boxing or reference
/// conversion to that type. Without a <see cref="Source"/>, it is the constant <see cref="Constant"/> (its invariant text,
/// or null for <c>null</c>) of the IR type <see cref="ConstantType"/>: <c>bool</c>, <c>bv</c><i>n</i>, or a sort name.
/// </summary>
public sealed record ApiArgument(int? Source, bool Unwrap = false, string? ConvertTo = null, string? ConstantType = null, string? Constant = null);
