using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The synthesised inputs a lowered body needs beyond its C# parameters (VERIFICATION-MODEL.md
/// section 2; ticket M2-004): the receiver <c>this</c>, one <c>null.&lt;Sort&gt;</c> map per reference
/// sort whose nullness is read, one <c>field.&lt;Type&gt;.&lt;Field&gt;</c> map per field touched, and
/// <c>array.&lt;Sort&gt;</c> plus <c>length.&lt;Sort&gt;</c> per array sort indexed, each keyed by the array reference like
/// <c>null.&lt;Sort&gt;</c>, so two variables holding one array read one slice (ticket P1-006), and one <c>cast.&lt;From&gt;.&lt;To&gt;</c> map per implicit
/// reference or boxing conversion (ticket M3-010), whose result's nullness is over-approximated: it is read from
/// <c>null.&lt;To&gt;</c>, not tied to the operand's, one <c>istype.&lt;From&gt;.&lt;To&gt;</c> predicate per type test (ticket M4-005), and one <c>typeof.&lt;T&gt;</c> input per closed type read by
/// <c>typeof(T)</c> (ticket P2-002), which is never null and adds no trace event, and one <c>new.&lt;Sort&gt;</c> per array sort
/// created, from an allocation count to the array it allocates (ticket P2-001). Each is created once, on first use, and they become parameters ordered by name, so both
/// sides of a pair share them by name, while the C# parameters, which a caller binds by position, are shared by position (ADR 0021). IR variable names take
/// only letters, digits, <c>_</c>, <c>.</c> and <c>$</c>, so every part of a name is spelled with dots.
/// <c>field.*</c> and <c>array.*</c> are <see cref="IrParameterKind.Ref"/>: the body writes them and the final heap is an
/// observable (ADR 0018, ticket M3-007), so every exit names their final version in its outs. <c>this</c>, <c>null.*</c>,
/// <c>cast.*</c>, <c>istype.*</c>, <c>length.*</c>, <c>typeof.*</c> and <c>new.*</c> are <see cref="IrParameterKind.In"/>, because nothing the body does changes them,
/// except that a <c>length.*</c> the body writes, which only an array creation does, is <see cref="IrParameterKind.Ref"/> (ticket P2-001).
/// Every sort name, in a type and in an input's name, goes through <paramref name="sorts"/> (<see cref="TypeMapper"/>;
/// ticket M3-009).
/// </summary>
internal sealed class HeapInputs(Func<string, string> sorts)
{
    private static readonly IrBool Bool = new();

    private readonly Dictionary<string, IrVar> inputs = new(StringComparer.Ordinal);

    private readonly HashSet<string> written = new(StringComparer.Ordinal);

    /// <summary>The synthesised parameters, ordered by name.</summary>
    public ImmutableArray<IrParameter> Parameters =>
        [.. inputs.Values.OrderBy(static v => v.Name, StringComparer.Ordinal).Select(v => new IrParameter(v, IsWritable(v.Name) || written.Contains(v.Name) ? IrParameterKind.Ref : IrParameterKind.In))];

    /// <summary>The receiver of an instance method, as an uninterpreted value of its containing type.</summary>
    public IrVar This(INamedTypeSymbol type) => Input(IrParameterNames.Receiver, new IrSort(TypeMapper.MetadataName(type, sorts)));

    /// <summary>Whether each value of <paramref name="sort"/> is null; equal references are equally null.</summary>
    public IrVar Nulls(IrSort sort) => Input($"null.{Part(sort.Name)}", new IrMap(sort, Bool));

    /// <summary>
    /// One heap slice per field, from the receiver (a static field's is the type's token) to the field's value. A
    /// property's backing field is named for the property, so both sides agree whatever the compiler calls it, and an
    /// auto-property is one slice with a field of its name on the other side (ticket M4-008).
    /// </summary>
    public IrVar Field(IFieldSymbol field) =>
        Input(
            $"field.{Part(TypeMapper.MetadataName(field.ContainingType, sorts))}.{Part((field.AssociatedSymbol as IPropertySymbol)?.Name ?? field.Name)}",
            new IrMap(Receiver(field), TypeMapper.Map(field.Type, sorts)));

    /// <summary>The token a static field's map is keyed by: element 0 of its declaring type's sort.</summary>
    public IrSortValue Token(IFieldSymbol field) => new(Receiver(field).Name, 0);

    /// <summary>
    /// An implicit reference or boxing conversion from <paramref name="from"/> to <paramref name="to"/>, or a downcast whose
    /// type test passed (ticket M4-005), as an uninterpreted function (ticket M3-010): no trace event, the same operand always yields the same result. The
    /// result's nullness is read from <c>null.&lt;To&gt;</c> like any value's, not tied to the operand's; that
    /// over-approximates, since a real upcast or box of a non-null value is never null.
    /// </summary>
    public IrVar Cast(ITypeSymbol from, ITypeSymbol to) =>
        Input(
            $"cast.{Part(TypeMapper.MetadataName(from, sorts))}.{Part(TypeMapper.MetadataName(to, sorts))}",
            new IrMap(TypeMapper.Map(from, sorts), TypeMapper.Map(to, sorts)));

    /// <summary>
    /// Whether a value of <paramref name="from"/> is, at run time, of <paramref name="to"/> (ticket M4-005): a free predicate
    /// per pair of types, shared by both sides by name, read at a non-null value by <c>is</c>, <c>as</c>, a downcast and a
    /// type pattern. Nothing ties it to the type hierarchy, so it over-approximates.
    /// </summary>
    public IrVar IsType(ITypeSymbol from, ITypeSymbol to) =>
        Input(
            $"istype.{Part(TypeMapper.MetadataName(from, sorts))}.{Part(TypeMapper.MetadataName(to, sorts))}",
            new IrMap(TypeMapper.Map(from, sorts), Bool));

    /// <summary>
    /// The runtime type object <c>typeof(<paramref name="operand"/>)</c> reads for a closed type, of <paramref name="type"/>'s
    /// (<c>System.Type</c>) sort, shared by both sides by name (ticket P2-002).
    /// </summary>
    public IrVar TypeOf(ITypeSymbol operand, ITypeSymbol type) => Input($"typeof.{Part(TypeMapper.MetadataName(operand, sorts))}", TypeMapper.Map(type, sorts));

    /// <summary>The elements of every array of <paramref name="array"/>'s sort: from the array reference to its elements by bv32 index.</summary>
    public IrVar Elements(IrSort array, IrType element) => Input($"array.{Part(array.Name)}", new IrMap(array, new IrMap(new IrBitVec(32), element)));

    /// <summary>The length of every array of <paramref name="array"/>'s sort, by array reference.</summary>
    public IrVar Length(IrSort array) => Input($"length.{Part(array.Name)}", new IrMap(array, new IrBitVec(32)));

    /// <summary>
    /// The arrays of <paramref name="array"/>'s sort a body allocates, by allocation count: its first array creation of
    /// that sort is element 0, its second element 1 (ticket P2-001). Shared by name, so both sides' k-th allocations are
    /// one reference. Nothing ties it to the arrays the inputs reach, so the model may alias a fresh array with one of
    /// them; every real run is still a model, and the product only gains inputs.
    /// </summary>
    public IrVar Fresh(IrSort array) => Input($"new.{Part(array.Name)}", new IrMap(new IrBitVec(32), array));

    /// <summary>Marks <paramref name="input"/> as written by the body, which makes it <see cref="IrParameterKind.Ref"/>.</summary>
    public void Write(IrVar input) => written.Add(input.Name);

    /// <summary>A field map's key type. A field of a value type is keyed by the value, which is what value semantics mean.</summary>
    private IrSort Receiver(IFieldSymbol field) => new(TypeMapper.MetadataName(field.ContainingType, sorts));

    /// <summary>Whether <paramref name="name"/> is a <c>field.*</c> or <c>array.*</c> map, which is always <see cref="IrParameterKind.Ref"/>.</summary>
    public static bool IsWritable(string name) => name.StartsWith("field.", StringComparison.Ordinal) || name.StartsWith("array.", StringComparison.Ordinal);

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
