using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Fields and array elements as SSA maps (ticket M2-004 acceptance criterion 6): one heap slice per
/// field, keyed by its receiver, and one per array sort, keyed by the array reference and then by a bv32
/// index, bounded by the length map read at the same reference (ticket P1-006), so two variables holding
/// one array share its elements; each starts at its <see cref="HeapInputs"/> input, versioned like any
/// other SSA variable. <see cref="IrLowerer"/> reaches this through one instance (ticket P1-003).
/// Lowering an operand, null-checking a dereferenced receiver, and resolving an lvalue to its SSA
/// variable stay <see cref="IrLowerer"/>'s job, so this class calls back into it through the delegates
/// given at construction rather than naming its type. Sort names go through <paramref name="sorts"/> (ticket M3-009).
/// </summary>
internal sealed class HeapLowerer(
    SsaBuilder ssa,
    Func<IOperation, LoweringContext, IrVar> lower,
    Action<IOperation, IrVar, LoweringContext> throwIfNull,
    Func<IOperation, SsaBuilder.Variable?> resolveTarget,
    Action<LoweringContext, IrVar, string> throwIf,
    Func<string, string> sorts)
{
    private static readonly IrBool Bool = new();

    private static readonly IrBitVec Index = new(32);

    private readonly Dictionary<string, SsaBuilder.Variable> slices = new(StringComparer.Ordinal);

    private readonly Dictionary<string, SsaBuilder.Variable> counts = new(StringComparer.Ordinal);

    /// <summary>The synthesised heap inputs (<c>field.*</c>, <c>array.*</c>, <c>length.*</c>) this lowering has used so far.</summary>
    public HeapInputs Inputs { get; } = new(sorts);

    /// <summary>
    /// The outs of every <c>Ref</c> heap map. A heap map exists only once the body touches it, so these are taken after
    /// the whole body is lowered; SSA completes every exit in Build, and an exit that never writes the map names the map's
    /// input (ticket M3-007).
    /// </summary>
    public IEnumerable<(SsaBuilder.Variable Variable, IrVar Out)> Outs() =>
        Inputs.Parameters.Where(static p => p.Kind == IrParameterKind.Ref).Select(p => (slices[p.Var.Name], p.Var));

    /// <summary>
    /// The heap slices every call reads and writes (ticket P1-005): each <c>field.*</c> and <c>array.*</c> map the body touches.
    /// A <c>length.*</c> map the body writes is not one: only an array creation writes it, and a call cannot change the
    /// length of an array.
    /// </summary>
    public IEnumerable<SsaBuilder.Variable> CallHeap() =>
        Inputs.Parameters.Where(static p => HeapInputs.IsWritable(p.Var.Name)).Select(p => slices[p.Var.Name]);

    /// <summary>
    /// The heap slice an assignment target names, or null when it is not a field or a single-dimensional
    /// array element of a variable.
    /// </summary>
    public Access? Slice(IOperation lvalue, LoweringContext context) => lvalue switch
    {
        IFieldReferenceOperation field => Field(field, context),
        IArrayElementReferenceOperation element => Element(element, context),
        IPropertyReferenceOperation property => AutoProperty(property, context),
        _ => null,
    };

    /// <summary>
    /// A field is a map from its receiver, or from its declaring type's token when it is static. A receiver of a
    /// reference type is null-checked where the slice is read or written, not here (ticket P2-017).
    /// </summary>
    public Access Field(IFieldReferenceOperation field, LoweringContext context) => Member(field.Field, field.Instance, context);

    /// <summary>
    /// An auto-property whose accessors no override can replace is its backing field (ticket M4-008): its map is read and
    /// written where the accessors would be called, so a caller sees what they would do. Null, with nothing emitted, for
    /// any other property.
    /// </summary>
    public Access? AutoProperty(IPropertyReferenceOperation property, LoweringContext context) =>
        Inlined(property.Property) is { } field ? Member(field, property.Instance, context) : null;

    /// <summary>
    /// The map of <paramref name="field"/> at <paramref name="receiver"/>, or at its type's token when that is null; a
    /// receiver that is <c>this</c> is never null, so nothing is null-checked.
    /// </summary>
    public Access Backing(IFieldSymbol field, IrVar? receiver, LoweringContext context) =>
        new(Versioned(Inputs.Field(field)), Array: null, receiver ?? Const(Inputs.Token(field), context), Dereferenced: null);

    /// <summary>The backing field the compiler declares for <paramref name="property"/>, or null when it has none.</summary>
    public static IFieldSymbol? BackingField(IPropertySymbol property) =>
        property.ContainingType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));

    /// <summary>
    /// The backing field of a property that is neither virtual nor an override and whose accessors have no body, or null:
    /// a property with a body, or one an override may replace, is called.
    /// </summary>
    private static IFieldSymbol? Inlined(IPropertySymbol property) =>
        !property.IsVirtual && !property.IsOverride && Bodiless(property.GetMethod) && Bodiless(property.SetMethod) ? BackingField(property) : null;

    private static bool Bodiless(IMethodSymbol? accessor) =>
        accessor is null || accessor.DeclaringSyntaxReferences.All(static r => r.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null });

    /// <summary>
    /// A field is a map from its receiver, or from its declaring type's token when it is static. A receiver of a
    /// reference type is null-checked where the slice is read or written, not here (ticket P2-017).
    /// </summary>
    private Access Member(IFieldSymbol field, IOperation? instance, LoweringContext context)
    {
        SsaBuilder.Variable map = Versioned(Inputs.Field(field));
        if (instance is null)
        {
            return new Access(map, Array: null, Const(Inputs.Token(field), context), Dereferenced: null);
        }

        IOperation? dereferenced = instance.Type!.IsValueType ? null : instance;
        return new Access(map, Array: null, lower(instance, context), dereferenced);
    }

    /// <summary>
    /// An array element is the array's slice of its sort's map, read at the array reference, then a map from
    /// a bv32 index, bounded by the length map read at the same reference. The unsigned comparison catches a
    /// negative index too. Null when the array is neither a plain variable nor an array creation (ticket P2-001), the index is not bv32, or the array
    /// has several dimensions; nothing is emitted in that case.
    /// </summary>
    public Access? Element(IArrayElementReferenceOperation element, LoweringContext context)
    {
        if (element.Indices is not [{ Type: { } indexType }]
            || TypeMapper.Map(indexType) is not IrBitVec { Width: 32 }
            || (resolveTarget(element.ArrayReference) is null && element.ArrayReference is not IArrayCreationOperation))
        {
            return null;
        }

        IrVar reference = lower(element.ArrayReference, context);
        IrVar index = lower(element.Indices[0], context);
        return new Access(Versioned(Inputs.Elements((IrSort)reference.Type, TypeMapper.Map(element.Type!, sorts))), reference, index, element.ArrayReference);
    }

    /// <summary><c>a.Length</c> on an array variable is the length map read at its reference; every other property stays opaque.</summary>
    public IrVar? ArrayLength(IPropertyReferenceOperation property, LoweringContext context)
    {
        if (property is not { Property: { Name: "Length", ContainingType.SpecialType: SpecialType.System_Array }, Instance: { } instance }
            || resolveTarget(instance) is null)
        {
            return null;
        }

        IrVar reference = lower(instance, context);
        throwIfNull(instance, reference, context);
        return Length(reference, context);
    }

    public IrVar ReadSlice(Access access, LoweringContext context)
    {
        Check(access, context);
        IrVar map = ssa.Load(context.Current, access.Map);
        return MapRead(access.Array is { } array ? MapRead(map, array, context) : map, access.Key, context);
    }

    /// <summary>A field's map is written at its key; an array's slice is read, written at the index, and written back.</summary>
    public void WriteSlice(Access access, IrVar value, LoweringContext context)
    {
        Check(access, context);
        Store(access, value, context);
    }

    /// <summary>
    /// A new array of <paramref name="array"/>'s sort (ticket P2-001): the next element of <c>new.&lt;Sort&gt;</c> by this
    /// body's allocation count of that sort, whose length is written to <paramref name="length"/> and whose elements to
    /// <paramref name="default"/>. The caller has already thrown on a negative length.
    /// </summary>
    public IrVar Allocate(IrSort array, IrType element, IrVar length, IrValue @default, LoweringContext context)
    {
        SsaBuilder.Variable count = Count(Inputs.Fresh(array));
        IrVar allocated = ssa.Load(context.Current, count);
        IrVar reference = MapRead(Inputs.Fresh(array), allocated, context);
        ssa.Store(context.Current, count, Emit(IrBinaryOp.Add, allocated, Const(new IrBitVecValue(32, 1), context), Index, context));

        IrVar lengths = Inputs.Length(array);
        Inputs.Write(lengths);
        Overwrite(Versioned(lengths), reference, length, context);
        Overwrite(Versioned(Inputs.Elements(array, element)), reference, Const(new IrMapValue(new IrMap(Index, element), @default, []), context), context);
        return reference;
    }

    /// <summary>Element <paramref name="index"/> of a new array's initialiser: a write with no null or bounds check, as the index is in range.</summary>
    public void Initialize(IrVar array, int index, IrVar value, LoweringContext context) =>
        Store(new Access(Versioned(Inputs.Elements((IrSort)array.Type, value.Type)), array, Const(new IrBitVecValue(32, (ulong)index), context), Dereferenced: null), value, context);

    private void Store(Access access, IrVar value, LoweringContext context)
    {
        IrVar map = ssa.Load(context.Current, access.Map);
        IrVar updated = access.Array is { } array
            ? MapWrite(map, array, MapWrite(MapRead(map, array, context), access.Key, value, context), context)
            : MapWrite(map, access.Key, value, context);
        ssa.Store(context.Current, access.Map, updated);
    }

    public IrVar MapRead(IrVar map, IrVar key, LoweringContext context)
    {
        IrVar target = ssa.Temp(((IrMap)map.Type).Value);
        ssa.Emit(context.Current, new IrMapRead(target, map, key));
        return target;
    }

    /// <summary>Writes <paramref name="value"/> at <paramref name="key"/> of the current version of <paramref name="map"/>.</summary>
    private void Overwrite(SsaBuilder.Variable map, IrVar key, IrVar value, LoweringContext context) =>
        ssa.Store(context.Current, map, MapWrite(ssa.Load(context.Current, map), key, value, context));

    /// <summary>How many arrays of a sort the body has allocated so far, starting at 0 in the entry block.</summary>
    private SsaBuilder.Variable Count(IrVar fresh)
    {
        if (!counts.TryGetValue(fresh.Name, out SsaBuilder.Variable? variable))
        {
            variable = new SsaBuilder.Variable(new IrVar($"${fresh.Name}", Index));
            counts[fresh.Name] = variable;
            IrVar zero = ssa.Temp(Index);
            ssa.Emit(new IrBlockId(0), new IrConst(zero, new IrBitVecValue(32, 0)));
            ssa.Store(new IrBlockId(0), variable, zero);
        }

        return variable;
    }

    /// <summary>
    /// The SSA variable holding the current version of a heap slice, starting at its input. The input is stored ahead of
    /// everything else in the entry, so a call lowered before the body first touches the slice still reads it (ticket P1-005).
    /// </summary>
    private SsaBuilder.Variable Versioned(IrVar input)
    {
        if (!slices.TryGetValue(input.Name, out SsaBuilder.Variable? variable))
        {
            variable = new SsaBuilder.Variable(input);
            slices[input.Name] = variable;
            ssa.StoreFirst(new IrBlockId(0), variable, input);
        }

        return variable;
    }

    /// <summary>
    /// The checks the CLR makes at the <c>ldfld</c>/<c>stfld</c> or <c>ldelem</c>/<c>stelem</c>, after every operand, a
    /// written value included (ticket P2-017): a null receiver or array throws, then an index outside the array's length
    /// does; a field access has no bound.
    /// </summary>
    private void Check(Access access, LoweringContext context)
    {
        if (access.Dereferenced is { } dereferenced)
        {
            throwIfNull(dereferenced, access.Array ?? access.Key, context);
        }

        if (access.Array is { } array)
        {
            throwIf(context, Emit(IrBinaryOp.Uge, access.Key, Length(array, context), Bool, context), "System.IndexOutOfRangeException");
        }
    }

    /// <summary>The length of the array <paramref name="array"/> references, from its sort's length map.</summary>
    private IrVar Length(IrVar array, LoweringContext context) => MapRead(ssa.Load(context.Current, Versioned(Inputs.Length((IrSort)array.Type))), array, context);

    private IrVar MapWrite(IrVar map, IrVar key, IrVar value, LoweringContext context)
    {
        IrVar updated = ssa.Temp(map.Type);
        ssa.Emit(context.Current, new IrMapWrite(updated, map, key, value));
        return updated;
    }

    private IrVar Const(IrValue constant, LoweringContext context)
    {
        IrVar target = ssa.Temp(constant.Type);
        ssa.Emit(context.Current, new IrConst(target, constant));
        return target;
    }

    private IrVar Emit(IrBinaryOp op, IrVar left, IrVar right, IrType type, LoweringContext context)
    {
        IrVar target = ssa.Temp(type);
        ssa.Emit(context.Current, new IrBinary(target, op, left, right));
        return target;
    }

    /// <summary>
    /// One access to a heap slice: the SSA variable holding the map's current version, the array reference
    /// whose slice of it is accessed (null for a field, whose map is keyed directly), and the key: a field's
    /// receiver or an array's bv32 index, bounded by that array's length. <see cref="Dereferenced"/> is the operand
    /// whose value (the array, else the receiver key) is null-checked at the access, or null when it cannot be null.
    /// </summary>
    internal readonly record struct Access(SsaBuilder.Variable Map, IrVar? Array, IrVar Key, IOperation? Dereferenced);
}
