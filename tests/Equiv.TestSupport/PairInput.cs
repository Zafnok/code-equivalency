using System.Collections.Immutable;
using System.Globalization;

namespace Equiv.TestSupport;

/// <summary>
/// Arguments for a <see cref="PairGen"/> method, in parameter order, plus the static field's value before the call
/// (ticket M0-012). <see cref="SIsNull"/> chooses between <c>null</c> and a non-null string for <c>s</c>; <see cref="U"/>
/// is the array <c>u</c>, or null.
/// </summary>
public sealed record PairInput(int A, int B, long C, long D, bool E, bool SIsNull, int F, ImmutableArray<int>? U)
{
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"a={A} b={B} c={C} d={D} e={E} s={(SIsNull ? "null" : "\"s\"")} F={F} u={(U is { } u ? $"[{string.Join(',', u)}]" : "null")}");
}
