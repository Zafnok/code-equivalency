using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which interpolated strings are the concatenation of their parts whichever way they bind (ticket P2-086). The same text
/// binds <c>string.Format</c> on .NET Framework and <c>DefaultInterpolatedStringHandler</c> on .NET 6 and later. Both
/// append a <c>string</c> hole as it is, null as nothing, and format a hole of an 8- to 64-bit integer type as its
/// parameterless <c>ToString()</c> does, with the current culture. They differ in when: <c>string.Format</c> formats
/// after every hole is evaluated, the handler formats each hole before the next one is evaluated. So a string is covered
/// when every hole is a <c>string</c> or such an integer with no format or alignment clause, and every hole after the
/// first integer hole only reads a local, a parameter, a constant or a field of <c>this</c>: with no call and no throw
/// after the first formatting, the two orders are one. Any other hole type is formatted through members the two bindings
/// do not share (<c>IFormattable</c>, <c>ISpanFormattable</c>), and stays opaque.
/// </summary>
internal static class InterpolatedStrings
{
    /// <summary>Whether <paramref name="interpolated"/> is the concatenation of its parts under both bindings.</summary>
    public static bool IsConcatenation(IInterpolatedStringOperation interpolated)
    {
        IInterpolationOperation[] holes = [.. interpolated.Parts.OfType<IInterpolationOperation>()];
        return holes.All(static h => h.Alignment is null && h.FormatString is null && (IsInteger(h.Expression) || h.Expression.Type!.SpecialType == SpecialType.System_String))
            && holes.SkipWhile(static h => !IsInteger(h.Expression)).Skip(1).All(static h => IsRead(h.Expression));
    }

    /// <summary>Whether <paramref name="hole"/> is of an 8- to 64-bit integer type.</summary>
    public static bool IsInteger(IOperation hole) => hole.Type!.SpecialType
        is SpecialType.System_SByte
        or SpecialType.System_Byte
        or SpecialType.System_Int16
        or SpecialType.System_UInt16
        or SpecialType.System_Int32
        or SpecialType.System_UInt32
        or SpecialType.System_Int64
        or SpecialType.System_UInt64;

    /// <summary>Whether evaluating <paramref name="hole"/> calls nothing and cannot throw.</summary>
    private static bool IsRead(IOperation hole) =>
        hole.ConstantValue.HasValue || hole is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation { Instance: IInstanceReferenceOperation };
}
