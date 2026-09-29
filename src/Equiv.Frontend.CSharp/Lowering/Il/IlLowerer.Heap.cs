using System.Collections.Frozen;
using System.Globalization;
using System.Linq;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;

using SpecialType = Microsoft.CodeAnalysis.SpecialType;
using TypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// The IL lowering's heap (ticket P1-015; ADR 0015 and ADR 0018): an address is lowered only as an operand, to the place
/// it names, and <c>ldobj</c> and <c>stobj</c> read and write that place. A place is a variable (a local's or parameter's
/// address, a <c>ref</c> parameter, or <c>AddressOf</c>'s temporary), a heap slice <see cref="HeapLowerer"/> keys (a
/// field of an object at its receiver, a static field at its type's token, an array element at its reference and index,
/// with the CLR's null and bounds checks), or a field of a struct held at another place. A struct is a value: its fields
/// are maps keyed by it, as the IOperation lowering keys them, and a write of one field makes a new value, the next of
/// <c>new.&lt;Sort&gt;</c> with that field changed and every other copied, stored back where the struct was, so a copy never
/// sees it. Arrays are P1-006's value-keyed maps and P2-001's creation; type tests and boxing are M3-010's and M4-005's
/// <c>cast</c> and <c>istype</c> maps.
/// </summary>
internal sealed partial class IlLowerer
{
    /// <summary><c>decimal</c>'s static fields that are constants, which Roslyn reads for <c>0m</c>, <c>1m</c>, <c>-1m</c> and the limits.</summary>
    private static readonly FrozenDictionary<string, decimal> DecimalFields = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        [nameof(decimal.Zero)] = decimal.Zero,
        [nameof(decimal.One)] = decimal.One,
        [nameof(decimal.MinusOne)] = decimal.MinusOne,
        [nameof(decimal.MaxValue)] = decimal.MaxValue,
        [nameof(decimal.MinValue)] = decimal.MinValue,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The place each address instruction lowered names.</summary>
    private readonly Dictionary<ILInstruction, Place> places = [];

    /// <summary>The place each stack slot that holds an address names, evaluated where the slot is stored.</summary>
    private readonly Dictionary<ILVariable, Place> slots = [];

    private int temporaries;

    /// <summary>Whether <paramref name="instruction"/> yields an address; a store to a slot that holds one yields nothing the lowering reads.</summary>
    private static bool IsAddress(ILInstruction instruction) => instruction is not StLoc && instruction.ResultType == StackType.Ref;

    /// <summary>A stack slot that holds an address, which ILSpy stores once for a read-modify-write.</summary>
    private static bool IsAddressSlot(ILVariable variable) => variable is { Kind: VariableKind.StackSlot, Type: ByReferenceType };

    /// <summary>
    /// Whether <paramref name="address"/> names a place the IR has: a local's or parameter's address (not a caught
    /// exception's), a <c>ref</c>, <c>out</c> or <c>in</c> parameter, a slot holding one that was stored, a field that resolves (of an object,
    /// or of a struct at an address that is one), a static field, an element of an array by an <c>int</c> index, or a
    /// temporary. Not a struct's <c>this</c>, which the IOperation lowering does not model either.
    /// </summary>
    private bool Addressable(ILInstruction address) => address switch
    {
        LdLoca load => !caught.Contains(load.Variable) && VariableType(load.Variable) is not null,
        LdLoc { Variable: { Kind: VariableKind.Parameter, Index: >= 0 } } => true,
        LdLoc load => slots.ContainsKey(load.Variable),
        LdFlda field => Field(field.Field) is { } symbol && TypeMapper.TupleElement(symbol) is null && (!symbol.ContainingType.IsValueType || Addressable(field.Target)),
        LdsFlda field => Field(field.Field) is not null,
        LdElema element => element.Indices.Count == 1 && element.Indices[0].ResultType == StackType.I4 && symbols.Type(element.Type) is not null,
        AddressOf temporary => symbols.Type(temporary.Type) is not null,
        _ => false,
    };

    /// <summary>The field <paramref name="field"/> names in the loaded compilation, or null when it does not resolve or is a <c>ref</c> field.</summary>
    private IFieldSymbol? Field(IField field) =>
        symbols.Type(field.DeclaringType) is INamedTypeSymbol type
            ? type.GetMembers(field.Name).OfType<IFieldSymbol>().FirstOrDefault(static f => f.RefKind == RefKind.None)
            : null;

    /// <summary>
    /// The place an addressable <paramref name="address"/> names, its receiver and index evaluated now. An address ILSpy
    /// does not mark as delaying its exceptions null-checks its object, and bounds-checks its index, here, as the CLR does
    /// at the <c>ldflda</c> or <c>ldelema</c>; one it marks does so where it is read or written.
    /// </summary>
    private Place Address(ILInstruction address)
    {
        Place place = address switch
        {
            LdLoca load => new VariablePlace(Variable(load.Variable), VariableType(load.Variable)!),
            LdLoc { Variable.Kind: VariableKind.Parameter } load => new VariablePlace(variables[load.Variable], method.Parameters[load.Variable.Index!.Value].Type),
            LdLoc load => slots[load.Variable],
            LdFlda field => Member(field),
            LdsFlda field => new SlicePlace(heap.Backing(Field(field.Field)!, receiver: null, context), Field(field.Field)!.Type),
            LdElema element => Element(element),
            _ => Temporary((AddressOf)address),
        };
        places[address] = place;
        return place;
    }

    /// <summary>A field of a struct is a place in the struct's place; a field of an object is its map at the receiver, upcast to the field's type.</summary>
    private Place Member(LdFlda address)
    {
        IFieldSymbol field = Field(address.Field)!;
        if (field.ContainingType.IsValueType)
        {
            return new MemberPlace(Address(address.Target), field);
        }

        Val receiver = Receiver(address.Target, field.ContainingType);
        IrVar? isNull = Nullness(address.Target, receiver.Var);
        return new SlicePlace(heap.Backing(field, Upcast(receiver, field.ContainingType), context) with { IsNull = Checked(isNull, address.DelayExceptions) }, field.Type);
    }

    private SlicePlace Element(LdElema address)
    {
        ITypeSymbol element = symbols.Type(address.Type)!;
        IArrayTypeSymbol type = compilation.CreateArrayTypeSymbol(element);
        Val array = Receiver(address.Array, type);
        IrVar reference = Coerce(array, type)!;
        IrVar index = Value(address.Indices[0], Int32);
        IrVar? isNull = Checked(Nullness(address.Array, array.Var), address.DelayExceptions);
        if (!address.DelayExceptions)
        {
            ThrowIf(Emit(IrBinaryOp.Uge, index, heap.Length(reference, context), Bool), "System.IndexOutOfRangeException");
        }

        return new SlicePlace(heap.Element(reference, index, element, isNull), element);
    }

    /// <summary>
    /// A nullness to check where the place is read or written, or, when the address does not delay its exceptions, checked
    /// now, leaving none.
    /// </summary>
    private IrVar? Checked(IrVar? isNull, bool delayed)
    {
        if (isNull is not null && !delayed)
        {
            ThrowIf(isNull, NullReferenceException);
            return null;
        }

        return isNull;
    }

    /// <summary><c>AddressOf</c>: a fresh variable holding a copy of the value, so nothing written through it reaches the original.</summary>
    private VariablePlace Temporary(AddressOf address)
    {
        ITypeSymbol type = symbols.Type(address.Type)!;
        SsaBuilder.Variable variable = new(new IrVar($"$address{temporaries++.ToString(CultureInfo.InvariantCulture)}", Map(type)));
        ssa.Store(context.Current, variable, Value(address.Value, type));
        return new VariablePlace(variable, type);
    }

    /// <summary>The value at <paramref name="place"/>: a variable's, a slice read with its checks, or a struct's field map read at the struct.</summary>
    private Val Read(Place place) => place switch
    {
        VariablePlace local => new(ssa.Load(context.Current, local.Variable), local.Type),
        SlicePlace slice => new(heap.ReadSlice(slice.Access, context), slice.Type),
        _ => Field((MemberPlace)place, Read(((MemberPlace)place).Parent)),
    };

    private Val Field(MemberPlace member, Val instance) => new(heap.ReadSlice(heap.Backing(member.Field, instance.Var, context), context), member.Type);

    /// <summary>
    /// Writes <paramref name="value"/>, which <paramref name="source"/> made, at <paramref name="place"/>: a variable and its
    /// null shadow, a slice with its checks, or, for a struct's field, a new struct stored where the struct was.
    /// </summary>
    private void Write(Place place, IrVar value, ILInstruction source)
    {
        switch (place)
        {
            case VariablePlace local:
                ssa.Store(context.Current, local.Variable, value);
                if (Shadow(local.Variable) is { } shadow)
                {
                    ssa.Store(context.Current, shadow, NullFlag(source, value));
                }

                break;
            case SlicePlace slice:
                heap.WriteSlice(slice.Access, value, context);
                break;
            default:
                MemberPlace member = (MemberPlace)place;
                Write(member.Parent, With(Read(member.Parent), member.Field, value), source);
                break;
        }
    }

    /// <summary>A <c>ref</c> or <c>out</c> argument's variable, written with a value nothing is known about, its shadow read from <c>null.&lt;Sort&gt;</c>.</summary>
    private void WriteUnknown(VariablePlace place, IrVar value)
    {
        ssa.Store(context.Current, place.Variable, value);
        if (Shadow(place.Variable) is { } shadow)
        {
            ssa.Store(context.Current, shadow, heap.MapRead(heap.Inputs.Nulls((IrSort)value.Type), value, context));
        }
    }

    /// <summary>
    /// <paramref name="instance"/> with <paramref name="changed"/> set to <paramref name="value"/>: the next new element of
    /// its sort, whose every instance field is <paramref name="instance"/>'s but the one changed.
    /// </summary>
    private IrVar With(Val instance, IFieldSymbol changed, IrVar value)
    {
        IrVar fresh = heap.Fresh((IrSort)instance.Var.Type, context);
        foreach (IFieldSymbol field in instance.Type.GetMembers().OfType<IFieldSymbol>().Where(static f => !f.IsStatic))
        {
            IrVar kept = SymbolEqualityComparer.Default.Equals(field, changed) ? value : heap.ReadSlice(heap.Backing(field, instance.Var, context), context);
            heap.WriteSlice(heap.Backing(field, fresh, context), kept, context);
        }

        return fresh;
    }

    /// <summary>The value a heap, type-test or function instruction yields, or, for an integer or <c>null</c> constant, the one of <paramref name="hint"/>'s type.</summary>
    private Val Heap(ILInstruction instruction, ITypeSymbol hint) => instruction switch
    {
        LdObj load when load.Target is LdsFlda { Field: { DeclaringType.FullName: "System.Decimal" } field } && DecimalFields.TryGetValue(field.Name, out decimal constant) =>
            Literal(SpecialType.System_Decimal, constant),
        LdObj load => Read(Address(load.Target)),
        StObj store => Store(store),
        NewArr array => Allocate(array),
        LdLen length => Length(length),
        IsInst test => Test(test, test.Argument, test.Type, downcast: false),
        CastClass test => Test(test, test.Argument, test.Type, downcast: true),
        Box box => Box(box, hint),
        LdFtn function => Function(function.Method, hint),
        LdVirtFtn function => Function(function, hint),
        _ => new(Const(Constant(instruction, hint)), hint),
    };

    /// <summary><c>stobj</c>: the place, then the value as the place's type, then the write, whose value it yields.</summary>
    private Val Store(StObj store)
    {
        Place place = Address(store.Target);
        IrVar value = Value(store.Value, place.Type);
        Write(place, value, store.Value);
        return new(value, place.Type);
    }

    /// <summary>
    /// <c>new T[n]</c> (ticket P2-001): a negative length throws <c>System.OverflowException</c>, as <c>newarr</c> does, unless
    /// it is a constant, then a fresh array of that length holds <c>default(T)</c> everywhere.
    /// </summary>
    private Val Allocate(NewArr array)
    {
        ITypeSymbol element = symbols.Type(array.Type)!;
        IArrayTypeSymbol type = compilation.CreateArrayTypeSymbol(element);
        IrVar length = Value(array.Indices[0], Int32);
        if (array.Indices[0] is not LdcI4)
        {
            ThrowIf(Emit(IrBinaryOp.Slt, length, Const(new IrBitVecValue(32, 0)), Bool), PureCatalogue.Overflow);
        }

        return new(heap.Allocate((IrSort)Map(type), Map(element), length, TypeMapper.Default(element, TypeMapper.Unmapped)!, context), type);
    }

    /// <summary><c>a.Length</c>: the array null-checked, then its sort's length map read at it (ticket P1-006).</summary>
    private Val Length(LdLen length)
    {
        Val array = Receiver(length.Array, compilation.GetSpecialType(SpecialType.System_Array));
        ThrowIfNull(length.Array, array.Var);
        return new(heap.Length(array.Var, context), Int32);
    }

    /// <summary>
    /// <c>as</c> and a downcast (ticket M4-005): the operand's <c>istype</c> read, and the operand's <c>cast</c> when it
    /// passes; <c>as</c> yields <c>null</c> when it fails, and a downcast throws <c>System.InvalidCastException</c> on a
    /// non-null operand it fails on and passes a null one through. An operand of a type no reference conversion relates to
    /// the one tested, or of a value type or a type parameter, is lowered first, and the test is then opaque of its key.
    /// </summary>
    private Val Test(ILInstruction test, ILInstruction operand, IType tested, bool downcast)
    {
        ITypeSymbol to = symbols.Type(tested)!;
        Val value = Receiver(operand, Object);
        if (value.Type is not { IsReferenceType: true, TypeKind: not TypeKind.TypeParameter } from
            || compilation.ClassifyCommonConversion(from, to) is not { Exists: true } conversion
            || !(conversion.IsReference || conversion.IsIdentity))
        {
            return new(Opaque(test, IlKeys.Key(test, caught), Map(to), fingerprint: false)!, to);
        }

        IrVar? isNull = Nullness(operand, value.Var);
        if (conversion.IsImplicit)
        {
            // The operand is already of the tested type, as Roslyn's optimiser leaves a cast of a local it removed: every
            // non-null operand passes, and the value is the operand, upcast.
            nulls[test] = isNull ?? Const(new IrBoolValue(Value: false));
            return new(Coerce(value, to)!, to);
        }

        IrVar isType = heap.MapRead(heap.Inputs.IsType(from, to), value.Var, context);
        IrVar cast() => heap.MapRead(heap.Inputs.Cast(from, to), value.Var, context);
        IrVar @null() => Const(TypeMapper.Constant(to, value: null));
        if (!downcast)
        {
            IrVar passes = isNull is null ? isType : Emit(IrBinaryOp.And, Not(isNull), isType, Bool);
            nulls[test] = Not(passes);
            return new(Select(passes, cast(), @null()), to);
        }

        ThrowIf(Not(isNull is null ? isType : Emit(IrBinaryOp.Or, isNull, isType, Bool)), "System.InvalidCastException");
        nulls[test] = isNull ?? Const(new IrBoolValue(Value: false));
        return new(isNull is null ? cast() : Select(isNull, @null(), cast()), to);
    }

    /// <summary>
    /// A boxing conversion (ticket M3-010): the value's <c>cast</c> to the reference type it is used as,
    /// <paramref name="hint"/>, as the IOperation lowering's conversion to that type is (IL that verifies uses a box only
    /// as an <c>object</c> or an interface it implements).
    /// </summary>
    private Val Box(Box box, ITypeSymbol hint)
    {
        ITypeSymbol from = symbols.Type(box.Type)!;
        return new(heap.MapRead(heap.Inputs.Cast(from, hint), Value(box.Argument, from), context), hint);
    }

    /// <summary>A virtual method's pointer: its receiver, null-checked, then the method's element.</summary>
    private Val Function(LdVirtFtn function, ITypeSymbol hint)
    {
        Val receiver = Receiver(function.Argument, Object);
        ThrowIfNull(function.Argument, receiver.Var);
        return Function(function.Method, hint);
    }

    /// <summary>A named method's pointer, as a delegate constructor takes it: the element of <paramref name="hint"/>'s sort that its call identity designates.</summary>
    private Val Function(IMethod target, ITypeSymbol hint) =>
        new(Const(TypeMapper.Constant(hint, CallIdentityFactory.Of(symbols.Method(target)!, compilation, RenameMap.Empty, []).Value)), hint);

    /// <summary>A place an address names, with the C# type of what it holds.</summary>
    private abstract record Place(ITypeSymbol Type);

    /// <summary>A local, a parameter, or <c>AddressOf</c>'s temporary.</summary>
    private sealed record VariablePlace(SsaBuilder.Variable Variable, ITypeSymbol Type) : Place(Type);

    /// <summary>A field of an object, a static field or an array element: a slice of a heap map, with its checks.</summary>
    private sealed record SlicePlace(HeapLowerer.Access Access, ITypeSymbol Type) : Place(Type);

    /// <summary>A field of the struct held at <see cref="Parent"/>.</summary>
    private sealed record MemberPlace(Place Parent, IFieldSymbol Field) : Place(Field.Type);
}
