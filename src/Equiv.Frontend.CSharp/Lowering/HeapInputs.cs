using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The synthesised inputs a lowered body needs beyond its C# parameters (VERIFICATION-MODEL.md
/// section 2; ticket M2-004): the receiver <c>this</c>, one <c>null.&lt;Sort&gt;</c> map per reference
/// sort whose nullness is read, one <c>field.&lt;Type&gt;.&lt;Field&gt;</c> map per field touched, and
/// <c>array.&lt;v&gt;</c> plus <c>length.&lt;v&gt;</c> per array variable indexed. Each is created once, on first use, and they become <see cref="IrParameterKind.In"/> parameters ordered by name, so both
/// sides of a pair share them by name, while the C# parameters, which a caller binds by position, are shared by position (ADR 0021). IR variable names take
/// only letters, digits, <c>_</c>, <c>.</c> and <c>$</c>, so every part of a name is spelled with dots.
/// </summary>
internal sealed class HeapInputs
{
    private static readonly IrBool Bool = new();

    private readonly Dictionary<string, IrVar> inputs = new(StringComparer.Ordinal);

    /// <summary>The synthesised parameters, ordered by name.</summary>
    public ImmutableArray<IrParameter> Parameters =>
        [.. inputs.Values.OrderBy(static v => v.Name, StringComparer.Ordinal).Select(static v => new IrParameter(v, IrParameterKind.In))];

    /// <summary>The receiver of an instance method, as an uninterpreted value of its containing type.</summary>
    public IrVar This(INamedTypeSymbol type) => Input("this", new IrSort(TypeMapper.MetadataName(type)));

    /// <summary>Whether each value of <paramref name="sort"/> is null; equal references are equally null.</summary>
    public IrVar Nulls(IrSort sort) => Input($"null.{Part(sort.Name)}", new IrMap(sort, Bool));

    /// <summary>One heap slice per field, from the receiver (a static field's is the type's token) to the field's value.</summary>
    public IrVar Field(IFieldSymbol field) =>
        Input(
            $"field.{Part(TypeMapper.MetadataName(field.ContainingType))}.{Part(field.Name)}",
            new IrMap(Receiver(field), TypeMapper.Map(field.Type)));

    /// <summary>The token a static field's map is keyed by: element 0 of its declaring type's sort.</summary>
    public static IrSortValue Token(IFieldSymbol field) => new(Receiver(field).Name, 0);

    /// <summary>The elements of the array a variable holds, by bv32 index.</summary>
    public IrVar Elements(string variable, IrType element) => Input($"array.{Part(variable)}", new IrMap(new IrBitVec(32), element));

    /// <summary>The length of the array a variable holds.</summary>
    public IrVar Length(string variable) => Input($"length.{Part(variable)}", new IrBitVec(32));

    /// <summary>A field map's key type. A field of a value type is keyed by the value, which is what value semantics mean.</summary>
    private static IrSort Receiver(IFieldSymbol field) => new(TypeMapper.MetadataName(field.ContainingType));

    private static string Part(string name) =>
        string.Concat(name.Select(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' ? c : '_'));

    private IrVar Input(string name, IrType type)
    {
        if (!inputs.TryGetValue(name, out IrVar? input))
        {
            input = new IrVar(name, type);
            inputs[name] = input;
        }

        return input;
    }
}
