using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
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

    private readonly Dictionary<string, SsaBuilder.Variable> slices = new(StringComparer.Ordinal);

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
    /// The heap slice an assignment target names, or null when it is not a field or a single-dimensional
    /// array element of a variable.
    /// </summary>
    public Access? Slice(IOperation lvalue, LoweringContext context) => lvalue switch
    {
        IFieldReferenceOperation field => Field(field, context),
        IArrayElementReferenceOperation element => Element(element, context),
        _ => null,
    };

    /// <summary>A field is a map from its receiver, or from its declaring type's token when it is static.</summary>
    public Access Field(IFieldReferenceOperation field, LoweringContext context)
    {
        IrVar key;
        if (field.Instance is { } instance)
        {
            key = lower(instance, context);
            if (!instance.Type!.IsValueType)
            {
                throwIfNull(instance, key, context);
            }
        }
        else
        {
            key = Const(Inputs.Token(field.Field), context);
        }

        return new Access(Versioned(Inputs.Field(field.Field)), Array: null, key);
    }

    /// <summary>
    /// An array element is the array's slice of its sort's map, read at the array reference, then a map from
    /// a bv32 index, bounded by the length map read at the same reference. The unsigned comparison catches a
    /// negative index too. Null when the array is not a plain variable, the index is not bv32, or the array
    /// has several dimensions; nothing is emitted in that case.
    /// </summary>
    public Access? Element(IArrayElementReferenceOperation element, LoweringContext context)
    {
        if (element.Indices is not [{ Type: { } indexType }]
            || TypeMapper.Map(indexType) is not IrBitVec { Width: 32 }
            || resolveTarget(element.ArrayReference) is null)
        {
            return null;
        }

        IrVar reference = lower(element.ArrayReference, context);
        throwIfNull(element.ArrayReference, reference, context);
        IrVar index = lower(element.Indices[0], context);
        return new Access(Versioned(Inputs.Elements((IrSort)reference.Type, TypeMapper.Map(element.Type!, sorts))), reference, index);
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
        Bounds(access, context);
        IrVar map = ssa.Load(context.Current, access.Map);
        return MapRead(access.Array is { } array ? MapRead(map, array, context) : map, access.Key, context);
    }

    /// <summary>A field's map is written at its key; an array's slice is read, written at the index, and written back.</summary>
    public void WriteSlice(Access access, IrVar value, LoweringContext context)
    {
        Bounds(access, context);
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

    /// <summary>The SSA variable holding the current version of a heap slice, starting at its input.</summary>
    private SsaBuilder.Variable Versioned(IrVar input)
    {
        if (!slices.TryGetValue(input.Name, out SsaBuilder.Variable? variable))
        {
            variable = new SsaBuilder.Variable(input);
            slices[input.Name] = variable;
            ssa.Store(new IrBlockId(0), variable, input);
        }

        return variable;
    }

    /// <summary>An index outside the array's length throws; a field access has no bound.</summary>
    private void Bounds(Access access, LoweringContext context)
    {
        if (access.Array is { } array)
        {
            throwIf(context, Emit(IrBinaryOp.Uge, access.Key, Length(array, context), Bool, context), "System.IndexOutOfRangeException");
        }
    }

    /// <summary>The length of the array <paramref name="array"/> references, from its sort's length map.</summary>
    private IrVar Length(IrVar array, LoweringContext context) => MapRead(Inputs.Length((IrSort)array.Type), array, context);

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
    /// receiver or an array's bv32 index, bounded by that array's length.
    /// </summary>
    internal readonly record struct Access(SsaBuilder.Variable Map, IrVar? Array, IrVar Key);
}
