using System.Collections.Frozen;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// A <c>static readonly</c> field of a value or string type declared outside the compilation (ticket P2-250), such as
/// <c>IntPtr.Zero</c> or <c>String.Empty</c>: it has one value for the whole run, which no call the body makes can change,
/// since only its own type's static constructor writes it. Its <c>field.*</c> map is therefore an
/// <see cref="IrParameterKind.In"/> input no call writes, shared by both sides by name like any synthesised input, so
/// equal reads are equal values; and where this catalogue knows the value, a read is that constant and no input at all.
/// </summary>
internal static class FrameworkStatics
{
    /// <summary>
    /// The fields whose value is known, by name, as the C# value <see cref="TypeMapper.Constant(ITypeSymbol, object?, Func{string, string})"/>
    /// takes: <c>String.Empty</c> is <c>""</c>, and each other one its type's <c>default</c>, which is element 0 of the type's
    /// sort as P2-138 makes the <c>default</c> passed for an optional parameter. Each of those types' instance fields is
    /// private, so no body writes one.
    /// </summary>
    private static readonly FrozenDictionary<string, string?> Values = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["System.String.Empty"] = string.Empty,
        ["System.IntPtr.Zero"] = null,
        ["System.UIntPtr.Zero"] = null,
        ["System.Guid.Empty"] = null,
        ["System.TimeSpan.Zero"] = null,
        ["System.DateTime.MinValue"] = null,
        ["System.Drawing.Color.Empty"] = null,
        ["System.Drawing.Point.Empty"] = null,
        ["System.Drawing.PointF.Empty"] = null,
        ["System.Drawing.Size.Empty"] = null,
        ["System.Drawing.SizeF.Empty"] = null,
        ["System.Drawing.Rectangle.Empty"] = null,
        ["System.Drawing.RectangleF.Empty"] = null,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="field"/> is a <c>static readonly</c> field of a value or string type that no source in the compilation declares.</summary>
    public static bool IsFrozen(IFieldSymbol field) =>
        field is { IsStatic: true, IsReadOnly: true, IsConst: false, DeclaringSyntaxReferences.IsEmpty: true }
        && (field.Type.IsValueType || field.Type.SpecialType == SpecialType.System_String);

    /// <summary>
    /// The value of a frozen <paramref name="field"/> this catalogue knows, with sort names passed through
    /// <paramref name="sorts"/>, or null for any other field.
    /// </summary>
    public static IrValue? Known(IFieldSymbol field, Func<string, string> sorts) =>
        IsFrozen(field) && Values.TryGetValue(Name(field), out string? value) ? TypeMapper.Constant(field.Type, value, sorts) : null;

    /// <summary>The field's name after its type's namespace and metadata name, which no keyword (<c>nint</c>, <c>string</c>) replaces.</summary>
    private static string Name(IFieldSymbol field) =>
        $"{field.ContainingType.ContainingNamespace.ToDisplayString()}.{field.ContainingType.MetadataName}.{field.Name}";
}
