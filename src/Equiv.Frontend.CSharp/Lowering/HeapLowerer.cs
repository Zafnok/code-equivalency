using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Fields and array elements as SSA maps (ticket M2-004 acceptance criterion 6): one heap slice per
/// field, keyed by its receiver, and one per array variable, keyed by a bv32 index and bounded by the
/// variable's own length var; each starts at its <see cref="HeapInputs"/> input, versioned like any
/// other SSA variable. <see cref="IrLowerer"/> reaches this through one instance (ticket P1-003).
/// Lowering an operand, null-checking a dereferenced receiver, and resolving an lvalue to its SSA
/// variable stay <see cref="IrLowerer"/>'s job, so this class calls back into it through the delegates
/// given at construction rather than naming its type.
/// </summary>
internal sealed class HeapLowerer(
    SsaBuilder ssa,
    Func<IOperation, LoweringContext, IrVar> lower,
    Action<IOperation, IrVar, LoweringContext> throwIfNull,
    Func<IOperation, SsaBuilder.Variable?> resolveTarget,
    Action<LoweringContext, IrVar, string> throwIf)
{
    private static readonly IrBool Bool = new();

    private readonly Dictionary<string, SsaBuilder.Variable> slices = new(StringComparer.Ordinal);

    /// <summary>The synthesised heap inputs (<c>field.*</c>, <c>array.*</c>, <c>length.*</c>) this lowering has used so far.</summary>
    public HeapInputs Inputs { get; } = new();

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
            key = Const(HeapInputs.Token(field.Field), context);
        }

        return new Access(Versioned(Inputs.Field(field.Field)), key, Length: null);
    }

    /// <summary>
    /// An array element is a map from a bv32 index, bounded by the array variable's own length var. The
    /// unsigned comparison catches a negative index too. Null when the array is not a plain variable,
    /// the index is not bv32, or the array has several dimensions; nothing is emitted in that case.
    /// </summary>
    public Access? Element(IArrayElementReferenceOperation element, LoweringContext context)
    {
        if (element.Indices is not [{ Type: { } indexType }]
            || TypeMapper.Map(indexType) is not IrBitVec { Width: 32 }
            || resolveTarget(element.ArrayReference) is not { } array)
        {
            return null;
        }

        IrVar reference = lower(element.ArrayReference, context);
        throwIfNull(element.ArrayReference, reference, context);
        IrVar index = lower(element.Indices[0], context);
        return new Access(
            Versioned(Inputs.Elements(array.Template.Name, TypeMapper.Map(element.Type!))),
            index,
            Inputs.Length(array.Template.Name));
    }

    /// <summary><c>a.Length</c> on an array variable is that variable's length var; every other property stays opaque.</summary>
    public IrVar? ArrayLength(IPropertyReferenceOperation property, LoweringContext context)
    {
        if (property is not { Property: { Name: "Length", ContainingType.SpecialType: SpecialType.System_Array }, Instance: { } instance }
            || resolveTarget(instance) is not { } array)
        {
            return null;
        }

        throwIfNull(instance, lower(instance, context), context);
        return Inputs.Length(array.Template.Name);
    }

    public IrVar ReadSlice(Access access, LoweringContext context)
    {
        Bounds(access, context);
        return MapRead(ssa.Load(context.Current, access.Map), access.Key, context);
    }

    public void WriteSlice(Access access, IrVar value, LoweringContext context)
    {
        Bounds(access, context);
        IrVar map = ssa.Load(context.Current, access.Map);
        IrVar updated = ssa.Temp(map.Type);
        ssa.Emit(context.Current, new IrMapWrite(updated, map, access.Key, value));
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

    /// <summary>An index outside the array's own length var throws; a field access has no bound.</summary>
    private void Bounds(Access access, LoweringContext context)
    {
        if (access.Length is { } length)
        {
            throwIf(context, Emit(IrBinaryOp.Uge, access.Key, length, Bool, context), "System.IndexOutOfRangeException");
        }
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
    /// One access to a heap slice: the SSA variable holding the map's current version, the key, and the
    /// bound the key must be under (an array variable's length var; null for a field).
    /// </summary>
    internal readonly record struct Access(SsaBuilder.Variable Map, IrVar Key, IrVar? Length);
}
