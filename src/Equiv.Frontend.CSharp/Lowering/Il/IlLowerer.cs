using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;

using IlBlock = ICSharpCode.Decompiler.IL.Block;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// Lowers a method from its ILAst (ADR 0039; ticket P1-014) to the IR <see cref="IrLowerer"/> produces: the same
/// signature, parameters named as ADR 0021 names them, the same call identities, sorts and heap inputs, and the same
/// overflow, divide and call-threw edges. Mapped here are control flow (blocks, branches, leaves, <c>if</c> and
/// <c>switch</c>), locals, integral and <c>bool</c> constants, arithmetic, comparisons and conversions, and calls, an
/// auto-property's accessor being its backing field's map as <see cref="IrLowerer"/> makes it. Every other instruction,
/// and a mapped one whose operands the IR has no type for, is an <see cref="IrOpaque"/> whose reason is its
/// <see cref="IlKeys.Key"/> and whose span is its nearest sequence point; one that is a function of the locals it reads
/// carries <see cref="IlFragment"/>'s fingerprint, as a fragment of <see cref="IrLowerer"/> carries its own (ADR 0024
/// decision 2). No run uses this yet (ticket P1-016).
/// </summary>
internal sealed class IlLowerer
{
    private const string OverflowException = "System.OverflowException";

    private static readonly IrBool Bool = new();

    private readonly SsaBuilder ssa = new();
    private readonly LoweringContext context = new([], [], handlerExit: null);
    private readonly Dictionary<ILVariable, SsaBuilder.Variable> variables = [];
    private readonly Dictionary<SsaBuilder.Variable, SsaBuilder.Variable> shadows = [];
    private readonly Dictionary<IlBlock, IrBlockId> blocks = [];
    private readonly Dictionary<BlockContainer, IrBlockId> exits = [];
    private readonly Dictionary<string, IrBlockId> throws = new(StringComparer.Ordinal);
    private readonly IMethodSymbol method;
    private readonly Compilation compilation;
    private readonly IlSymbols symbols;
    private readonly HeapLowerer heap;
    private readonly IlFragment fragments;
    private readonly IlAstReader.Module module;
    private readonly MethodDefinitionHandle handle;
    private readonly HashSet<ILVariable> caught;
    private readonly SourceSpan bodySpan;
    private readonly IrType? returnType;
    private int selects;

    private IlLowerer(IMethodSymbol method, Compilation compilation, IlAstReader.Body body, SourceSpan bodySpan, IrType? returnType)
    {
        this.method = method;
        this.compilation = compilation;
        this.bodySpan = bodySpan;
        this.returnType = returnType;
        module = body.Module!;
        handle = (MethodDefinitionHandle)body.Function!.Method!.MetadataToken;
        caught = IlKeys.CaughtException(body.Function);
        symbols = new IlSymbols(compilation, method);
        fragments = new IlFragment(symbols, compilation);
        // The IL lowering reaches only the members of HeapLowerer keyed by a lowered value, never those that lower an
        // IOperation, so it gives none of the callbacks those need.
        heap = new HeapLowerer(ssa, lower: null!, typeOf: null!, throwIfNull: null!, resolveTarget: null!, throwIf: null!, TypeMapper.Unmapped);
    }

    private ITypeSymbol Boolean => compilation.GetSpecialType(SpecialType.System_Boolean);

    private ITypeSymbol Int32 => compilation.GetSpecialType(SpecialType.System_Int32);

    /// <summary>
    /// <paramref name="method"/>'s body, read from <paramref name="compilation"/>'s IL. A method with no ILAst is one
    /// whole-body opaque whose reason is <see cref="IlAstReader"/>'s.
    /// </summary>
    public static IrProcedure Lower(IMethodSymbol method, Compilation compilation)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        (ImmutableArray<IrParameter> parameters, IrType? returnType) = IrLowerer.Signature(method, TypeMapper.Unmapped);
        SourceSpan span = CSharpFrontend.ToSourceSpan(method.DeclaringSyntaxReferences is [var syntax, ..] ? syntax.GetSyntax().GetLocation() : method.Locations[0]);
        IlAstReader.Body body = IlAstReader.Read(method, compilation);
        return body.Failure is { } failure
            ? IrLowerer.Opaque(RoslynIdentity.Of(method, RenameMap.Empty), parameters, returnType, failure, [span])
            : new IlLowerer(method, compilation, body, span, returnType).Procedure(parameters, body.Function!);
    }

    /// <summary>The procedure: the C# parameters, then the heap inputs the lowering used, as <see cref="IrLowerer"/> orders them.</summary>
    private IrProcedure Procedure(ImmutableArray<IrParameter> parameters, ILFunction function)
    {
        IrBlockId start = ssa.NewBlock();
        context.Current = start;
        SsaBuilder.Variable[] declared = new SsaBuilder.Variable[parameters.Length];
        ImmutableArray<(SsaBuilder.Variable, IrVar)>.Builder outs = ImmutableArray.CreateBuilder<(SsaBuilder.Variable, IrVar)>();
        for (int i = 0; i < parameters.Length; i++)
        {
            declared[i] = Shadowed(parameters[i].Var, method.Parameters[i].Type);
            ssa.Store(start, declared[i], parameters[i].Var);
            if (Shadow(declared[i]) is { } shadow)
            {
                ssa.Store(start, shadow, heap.MapRead(heap.Inputs.Nulls((IrSort)parameters[i].Var.Type), parameters[i].Var, context));
            }

            if (parameters[i].Kind != IrParameterKind.In)
            {
                outs.Add((declared[i], parameters[i].Var));
            }
        }

        foreach (ILVariable parameter in function.Variables.Where(static v => v.Kind == VariableKind.Parameter && v.Index >= 0))
        {
            variables[parameter] = declared[parameter.Index!.Value];
        }

        Container((BlockContainer)function.Body);
        outs.AddRange(heap.Outs());
        IrProcedure procedure = new(
            RoslynIdentity.Of(method, RenameMap.Empty),
            [.. parameters, .. heap.Inputs.Parameters],
            returnType,
            ssa.Build(start, outs.ToImmutable(), [.. heap.CallHeap()], bodySpan),
            start);
        System.Diagnostics.Debug.Assert(IrValidator.Validate(procedure).IsEmpty, "lowered IR must validate");
        return procedure;
    }

    /// <summary>
    /// A container: its blocks, entered at its entry point; a <c>leave</c> of it goes on to what follows it, except that
    /// one leaving the function returns.
    /// </summary>
    private void Container(BlockContainer container)
    {
        exits[container] = ssa.NewBlock();
        foreach (IlBlock block in container.Blocks)
        {
            blocks[block] = ssa.NewBlock();
        }

        Jump(blocks[container.EntryPoint]);
        foreach (IlBlock block in container.Blocks)
        {
            context.Current = blocks[block];
            Sequence(block);
        }

        context.Current = exits[container];
    }

    private void Sequence(IlBlock block)
    {
        foreach (ILInstruction instruction in block.Instructions)
        {
            Statement(instruction);
        }
    }

    private void Statement(ILInstruction instruction)
    {
        string key = IlKeys.Key(instruction, caught);
        if (!IlKeys.Lowered.Contains(key) && instruction.HasFlag(InstructionFlags.EndPointUnreachable))
        {
            // A throw, or a region every path of which leaves: its opaque stands for where the method goes next.
            Terminate(new IrReturn(Opaque(instruction, key, returnType), []));
            return;
        }

        if (!IlKeys.Lowered.Contains(key))
        {
            _ = Opaque(instruction, key, type: null);
            return;
        }

        if (!Lowerable(instruction))
        {
            _ = Refused(instruction, key, type: null);
            return;
        }

        switch (instruction)
        {
            case Nop:
                return;
            case StLoc store:
                Store(store);
                return;
            case Branch branch:
                Jump(blocks[branch.TargetBlock]);
                return;
            case Leave leave:
                Leave(leave);
                return;
            case IfInstruction choice:
                If(choice);
                return;
            case BlockContainer container:
                Container(container);
                return;
            case IlBlock block:
                Sequence(block);
                return;
            case SwitchInstruction choice:
                Switch(choice);
                return;
            case CallInstruction call:
                _ = Call(call);
                return;
            default:
                _ = Natural(instruction, Int32);
                return;
        }
    }

    /// <summary>
    /// Whether an instruction whose key is mapped is lowered: the IR has its operands' types. Decided before anything of it
    /// is emitted.
    /// </summary>
    private bool Lowerable(ILInstruction instruction) => instruction switch
    {
        LdLoc load => VariableType(load.Variable) is not null,
        StLoc store => VariableType(store.Variable) is not null,
        BinaryNumericInstruction binary => binary.LeftInputType is StackType.I4 or StackType.I8 && binary.RightInputType is StackType.I4 or StackType.I8,
        Comp comparison => comparison.InputType is StackType.I4 or StackType.I8,
        Conv conversion => conversion.InputType is StackType.I4 or StackType.I8 && conversion.TargetType is >= PrimitiveType.I1 and <= PrimitiveType.U8,
        CallInstruction call => symbols.Method(call.Method) is { RefKind: RefKind.None } target
            && call.ConstrainedTo is null
            && target.Parameters.All(static p => p.RefKind == RefKind.None),
        _ => true,
    };

    /// <summary>
    /// A mapped instruction the IR has no types for, such as a comparison of references or a call with a <c>ref</c>
    /// argument: its operands are lowered, each unmapped one an opaque of its own key, and then it is an opaque with its key.
    /// </summary>
    private IrVar? Refused(ILInstruction instruction, string key, IrType? type)
    {
        foreach (ILInstruction operand in instruction.Children)
        {
            Statement(operand);
        }

        return Opaque(instruction, key, type, fingerprint: false);
    }

    /// <summary>The value of <paramref name="instruction"/> as a value of <paramref name="expected"/>'s IR type.</summary>
    private IrVar Value(ILInstruction instruction, ITypeSymbol expected)
    {
        string key = IlKeys.Key(instruction, caught);
        if (!IlKeys.Lowered.Contains(key))
        {
            return Opaque(instruction, key, Map(expected))!;
        }

        if (!Lowerable(instruction))
        {
            return Refused(instruction, key, Map(expected))!;
        }

        if (instruction is LdcI4 or LdcI8 or LdNull)
        {
            return Const(Constant(instruction, expected));
        }

        // A value of a type no conversion takes to the one wanted, such as an enum's as an integer, is opaque.
        return Coerce(Natural(instruction, expected), expected) ?? Opaque(instruction, key, Map(expected), fingerprint: false)!;
    }

    /// <summary>
    /// The value of a lowered <paramref name="instruction"/> with its own type: a local's, an operator's, a call's result.
    /// A constant, which has none, takes <paramref name="hint"/>'s.
    /// </summary>
    private Val Natural(ILInstruction instruction, ITypeSymbol hint) => instruction switch
    {
        LdLoc load => Load(load.Variable),
        LdStr text => new(Const(TypeMapper.Constant(compilation.GetSpecialType(SpecialType.System_String), text.Value)), compilation.GetSpecialType(SpecialType.System_String)),
        BinaryNumericInstruction binary => Binary(binary),
        Comp comparison => new(Compare(comparison), Boolean),
        Conv conversion => Convert(conversion),
        CallInstruction call => Call(call)!.Value,
        _ => new(Const(Constant(instruction, hint)), hint),
    };

    /// <summary>
    /// <paramref name="value"/> as a value of <paramref name="type"/>: a bitvector extended by its own type's sign or
    /// truncated, as IL's stack does; a bitvector as Bool is whether it is not zero, and Bool as a bitvector 1 or 0; a
    /// reference as another reference type through an implicit reference conversion is its <c>cast</c> map read (ticket
    /// M3-010). Null, with nothing emitted, when none of those relates the two.
    /// </summary>
    private IrVar? Coerce(Val value, ITypeSymbol type) => (value.Var.Type, Map(type)) switch
    {
        var (from, to) when from == to => value.Var,
        (IrBitVec, IrBitVec to) => Resize(value.Var, to, TypeMapper.IsSigned(value.Type)),
        (IrBitVec from, IrBool) => Emit(IrBinaryOp.Ne, value.Var, Const(new IrBitVecValue(from.Width, 0)), Bool),
        (IrBool, IrBitVec to) => Select(value.Var, Const(new IrBitVecValue(to.Width, 1)), Const(new IrBitVecValue(to.Width, 0))),
        (IrSort, IrSort) when compilation.ClassifyCommonConversion(value.Type, type) is { IsImplicit: true, IsReference: true } =>
            heap.MapRead(heap.Inputs.Cast(value.Type, type), value.Var, context),
        _ => null,
    };

    /// <summary>
    /// A constant as a value of <paramref name="type"/>: <c>null</c> element 0 of its sort, an integer that type's
    /// bitvector, Bool (not zero) or, for an enum, the element <see cref="TypeMapper.Constant(ITypeSymbol, object?)"/>
    /// gives its underlying value, as the IOperation lowering spells the same constant.
    /// </summary>
    private static IrValue Constant(ILInstruction constant, ITypeSymbol type) => (constant, TypeMapper.Map(type)) switch
    {
        (LdNull, _) => TypeMapper.Constant(type, value: null),
        (_, IrBitVec bits) => IrBitVecValue.FromSigned(bits.Width, Integer(constant)),
        (_, IrBool) => new IrBoolValue(Integer(constant) != 0),
        _ => TypeMapper.Constant(type, Integer(constant)),
    };

    private static long Integer(ILInstruction constant) => constant is LdcI4 small ? small.Value : ((LdcI8)constant).Value;

    /// <summary>
    /// A local or parameter's current value; <c>this</c> is the receiver input, as the IOperation lowering reads it where
    /// it is referenced.
    /// </summary>
    private Val Load(ILVariable variable) => IsThis(variable)
        ? new(heap.Inputs.This(method.ContainingType), method.ContainingType)
        : new(ssa.Load(context.Current, Variable(variable)), VariableType(variable)!);

    private void Store(StLoc store)
    {
        SsaBuilder.Variable variable = Variable(store.Variable);
        IrVar value = Value(store.Value, VariableType(store.Variable)!);
        ssa.Store(context.Current, variable, value);
        if (Shadow(variable) is { } shadow)
        {
            ssa.Store(context.Current, shadow, NullFlag(store.Value, value));
        }
    }

    private static bool IsThis(ILVariable variable) => variable is { Kind: VariableKind.Parameter, Index: -1 };

    /// <summary>A variable's type in the loaded compilation, or null when the IR has none for it, such as a <c>ref</c>'s.</summary>
    private ITypeSymbol? VariableType(ILVariable variable) => symbols.Type(variable.Type);

    /// <summary>A local's SSA variable, named as its PDB names it, with a null shadow when it is a reference the source declares or a stack slot.</summary>
    private SsaBuilder.Variable Variable(ILVariable variable)
    {
        if (!variables.TryGetValue(variable, out SsaBuilder.Variable? declared))
        {
            ITypeSymbol type = VariableType(variable)!;
            string name = string.Concat(variable.Name!.Select(static c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
            IrVar template = new(name, Map(type), variable.Name);
            // A local the compiler adds, such as Debug's return temporary, has no PDB name and no shadow: the IOperation
            // lowering has no variable for it, so its value's nullness is read where the value is used. A stack slot is
            // what the IOperation lowering's flow capture is, and has one.
            declared = variable is { Kind: VariableKind.Local, HasGeneratedName: true } ? new SsaBuilder.Variable(template) : Shadowed(template, type);
            variables[variable] = declared;
        }

        return declared;
    }

    /// <summary>A reference-typed variable is paired with a Bool <c>&lt;name&gt;.isNull</c> shadow, as the IOperation lowering pairs it.</summary>
    private SsaBuilder.Variable Shadowed(IrVar template, ITypeSymbol type)
    {
        SsaBuilder.Variable variable = new(template);
        if (type.IsReferenceType && template.Type is IrSort)
        {
            shadows[variable] = new SsaBuilder.Variable(new IrVar($"{template.Name}.isNull", Bool, template.SourceName));
        }

        return variable;
    }

    private SsaBuilder.Variable? Shadow(SsaBuilder.Variable variable) => shadows.GetValueOrDefault(variable);

    /// <summary>
    /// Whether <paramref name="source"/>'s value is null, or null when it provably is not: a <c>new</c> and <c>this</c> are
    /// not, <c>null</c> is, a variable carries its shadow, and anything else asks the <c>null.&lt;Sort&gt;</c> map.
    /// </summary>
    private IrVar? Nullness(ILInstruction source, IrVar value) => source switch
    {
        NewObj => null,
        LdLoc load when IsThis(load.Variable) => null,
        LdLoc load when variables.TryGetValue(load.Variable, out SsaBuilder.Variable? variable) && Shadow(variable) is { } shadow => ssa.Load(context.Current, shadow),
        LdNull => Const(new IrBoolValue(Value: true)),
        _ => heap.MapRead(heap.Inputs.Nulls((IrSort)value.Type), value, context),
    };

    private IrVar NullFlag(ILInstruction source, IrVar value) => Nullness(source, value) ?? Const(new IrBoolValue(Value: false));

    private void ThrowIfNull(ILInstruction source, IrVar value)
    {
        if (Nullness(source, value) is { } isNull)
        {
            ThrowIf(isNull, "System.NullReferenceException");
        }
    }

    private void Leave(Leave leave)
    {
        if (leave.IsLeavingFunction)
        {
            Terminate(new IrReturn(returnType is null ? null : Value(leave.Value, ReturnType()), []));
        }
        else
        {
            Jump(exits[leave.TargetContainer]);
        }
    }

    /// <summary>The type <see cref="IrLowerer.Signature"/> returns: an async method's task result, else the return type.</summary>
    private ITypeSymbol ReturnType() =>
        method is { IsAsync: true, IsIterator: false, ReturnType: INamedTypeSymbol { TypeArguments: [var result] } } ? result : method.ReturnType;

    private void If(IfInstruction choice)
    {
        IrVar condition = Value(choice.Condition, Boolean);
        IrBlockId then = ssa.NewBlock();
        IrBlockId otherwise = ssa.NewBlock();
        IrBlockId join = ssa.NewBlock();
        Terminate(new IrBranch(condition, then, otherwise));
        context.Current = then;
        Statement(choice.TrueInst);
        Jump(join);
        context.Current = otherwise;
        Statement(choice.FalseInst);
        Jump(join);
        context.Current = join;
    }

    /// <summary>
    /// A switch on an integer: each section other than the default is a case per label, each label the value's own
    /// width's bits, and each section's body, a branch or a leave, is lowered in a block of its own.
    /// </summary>
    private void Switch(SwitchInstruction choice)
    {
        IrVar scrutinee = Value(choice.Value, Stack(choice.Value.ResultType));
        int width = ((IrBitVec)scrutinee.Type).Width;
        SwitchSection fallback = choice.GetDefaultSection();
        Dictionary<SwitchSection, IrBlockId> targets = choice.Sections.ToDictionary(static s => s, _ => ssa.NewBlock());
        Terminate(new IrSwitch(
            scrutinee,
            [.. choice.Sections.Where(s => s != fallback).SelectMany(s => s.Labels.Values.Select(label => ((IrValue)IrBitVecValue.FromSigned(width, label), targets[s])))],
            targets[fallback]));
        foreach (SwitchSection section in choice.Sections)
        {
            context.Current = targets[section];
            Statement(section.Body);
        }
    }

    /// <summary>
    /// An integral operator, or <c>&amp;</c>, <c>|</c> or <c>^</c> of two Bool values. Its <see cref="Sign"/>, not its
    /// operands' types, decides a signed or unsigned division, remainder, right shift and overflow check.
    /// </summary>
    private Val Binary(BinaryNumericInstruction binary)
    {
        bool signed = binary.Sign == Sign.Signed;
        IrBinaryOp op = Operation(binary.Operator, signed);
        if (op is IrBinaryOp.And or IrBinaryOp.Or or IrBinaryOp.Xor && IsBool(binary.Left) && IsBool(binary.Right))
        {
            return new(Emit(op, Value(binary.Left, Boolean), Value(binary.Right, Boolean), Bool), Boolean);
        }

        ITypeSymbol type = Stack(binary.LeftInputType);
        IrVar left = Value(binary.Left, type);
        IrVar right = Value(binary.Right, OperatorMapper.IsShift(op) ? Stack(binary.RightInputType) : type);
        IrVar result = op switch
        {
            IrBinaryOp.SDiv or IrBinaryOp.SRem or IrBinaryOp.UDiv or IrBinaryOp.URem => Divide(op, left, right, signed),
            _ when OperatorMapper.IsShift(op) => Shift(op, left, right),
            _ => Arithmetic(op, left, right, binary.CheckForOverflow, signed),
        };
        return new(result, type);
    }

    /// <summary>The IR operator of <paramref name="op"/>; <see cref="BinaryNumericOperator.ShiftRight"/> is the last.</summary>
    private static IrBinaryOp Operation(BinaryNumericOperator op, bool signed) => op switch
    {
        BinaryNumericOperator.Add => IrBinaryOp.Add,
        BinaryNumericOperator.Sub => IrBinaryOp.Sub,
        BinaryNumericOperator.Mul => IrBinaryOp.Mul,
        BinaryNumericOperator.Div => signed ? IrBinaryOp.SDiv : IrBinaryOp.UDiv,
        BinaryNumericOperator.Rem => signed ? IrBinaryOp.SRem : IrBinaryOp.URem,
        BinaryNumericOperator.BitAnd => IrBinaryOp.And,
        BinaryNumericOperator.BitOr => IrBinaryOp.Or,
        BinaryNumericOperator.BitXor => IrBinaryOp.Xor,
        BinaryNumericOperator.ShiftLeft => IrBinaryOp.Shl,
        _ => signed ? IrBinaryOp.AShr : IrBinaryOp.LShr,
    };

    /// <summary>A division or remainder throws on a zero divisor and, signed, on <c>MinValue</c> by -1, as .NET does unchecked too.</summary>
    private IrVar Divide(IrBinaryOp op, IrVar left, IrVar right, bool signed)
    {
        ThrowIf(Emit(IrBinaryOp.Eq, right, Const(new IrBitVecValue(((IrBitVec)right.Type).Width, 0)), Bool), "System.DivideByZeroException");
        if (signed)
        {
            ThrowIfOverflows(IrOverflowOp.SDiv, left, right);
        }

        return Emit(op, left, right, left.Type);
    }

    /// <summary>The count is masked to the value's width and then made that width, as <see cref="IrLowerer"/> shifts.</summary>
    private IrVar Shift(IrBinaryOp op, IrVar left, IrVar count)
    {
        IrBitVec type = (IrBitVec)left.Type;
        IrVar mask = Const(new IrBitVecValue(((IrBitVec)count.Type).Width, (ulong)type.Width - 1));
        return Emit(op, left, Resize(Emit(IrBinaryOp.And, count, mask, count.Type), type, signed: false), type);
    }

    private IrVar Arithmetic(IrBinaryOp op, IrVar left, IrVar right, bool isChecked, bool signed)
    {
        if (isChecked && OperatorMapper.Overflow(op, signed) is { } overflow)
        {
            ThrowIfOverflows(overflow, left, right);
        }

        return Emit(op, left, right, left.Type);
    }

    /// <summary>An integral comparison by its <see cref="Sign"/>, or an equality of two values one of which is Bool.</summary>
    private IrVar Compare(Comp comparison)
    {
        if (comparison is { Kind: ComparisonKind.Equality or ComparisonKind.Inequality, InputType: StackType.I4 } && (IsBool(comparison.Left) || IsBool(comparison.Right)))
        {
            return Emit(comparison.Kind == ComparisonKind.Equality ? IrBinaryOp.Eq : IrBinaryOp.Ne, Value(comparison.Left, Boolean), Value(comparison.Right, Boolean), Bool);
        }

        ITypeSymbol type = Stack(comparison.InputType);
        IrVar left = Value(comparison.Left, type);
        IrVar right = Value(comparison.Right, type);
        bool unsigned = comparison.Sign == Sign.Unsigned;
        IrBinaryOp op = comparison.Kind switch
        {
            ComparisonKind.Equality => IrBinaryOp.Eq,
            ComparisonKind.Inequality => IrBinaryOp.Ne,
            ComparisonKind.LessThan => unsigned ? IrBinaryOp.Ult : IrBinaryOp.Slt,
            ComparisonKind.LessThanOrEqual => unsigned ? IrBinaryOp.Ule : IrBinaryOp.Sle,
            ComparisonKind.GreaterThan => unsigned ? IrBinaryOp.Ugt : IrBinaryOp.Sgt,
            _ => unsigned ? IrBinaryOp.Uge : IrBinaryOp.Sge,
        };
        return Emit(op, left, right, Bool);
    }

    /// <summary>Whether <paramref name="instruction"/>'s value is a C# <c>bool</c>, which the IR keeps as Bool although IL's stack has an int.</summary>
    private bool IsBool(ILInstruction instruction) => instruction switch
    {
        Comp => true,
        LdLoc load => VariableType(load.Variable) is { SpecialType: SpecialType.System_Boolean },
        CallInstruction call => symbols.Method(call.Method) is { ReturnType.SpecialType: SpecialType.System_Boolean },
        BinaryNumericInstruction { Operator: BinaryNumericOperator.BitAnd or BinaryNumericOperator.BitOr or BinaryNumericOperator.BitXor } binary =>
            IsBool(binary.Left) && IsBool(binary.Right),
        _ => false,
    };

    /// <summary>
    /// An integral conversion: extension by its kind, truncation, or a change of sign alone; a checked one throws when the
    /// value does not fit, its input read with the conversion's input sign.
    /// </summary>
    private Val Convert(Conv conversion)
    {
        IrVar value = Value(conversion.Argument, Stack(conversion.InputType));
        ITypeSymbol target = compilation.GetSpecialType(conversion.TargetType switch
        {
            PrimitiveType.I1 => SpecialType.System_SByte,
            PrimitiveType.U1 => SpecialType.System_Byte,
            PrimitiveType.I2 => SpecialType.System_Int16,
            PrimitiveType.U2 => SpecialType.System_UInt16,
            PrimitiveType.I4 => SpecialType.System_Int32,
            PrimitiveType.U4 => SpecialType.System_UInt32,
            PrimitiveType.I8 => SpecialType.System_Int64,
            _ => SpecialType.System_UInt64,
        });
        IrVar result = Resize(value, (IrBitVec)Map(target), conversion.Kind == ConversionKind.SignExtend);
        if (conversion.CheckForOverflow)
        {
            ThrowIfItDoesNotFit(value, result, conversion.InputSign == Sign.Signed, TypeMapper.IsSigned(target));
        }

        return new(result, target);
    }

    /// <summary>Throws when <paramref name="result"/> does not round-trip to <paramref name="value"/>, or when the signed side of a signedness change is negative.</summary>
    private void ThrowIfItDoesNotFit(IrVar value, IrVar result, bool fromSigned, bool toSigned)
    {
        IrVar lost = Emit(IrBinaryOp.Ne, Resize(result, (IrBitVec)value.Type, toSigned), value, Bool);
        if (fromSigned != toSigned)
        {
            IrVar signedSide = fromSigned ? value : result;
            lost = Emit(IrBinaryOp.Or, lost, Emit(IrBinaryOp.Slt, signedSide, Const(new IrBitVecValue(((IrBitVec)signedSide.Type).Width, 0)), Bool), Bool);
        }

        ThrowIf(lost, OverflowException);
    }

    /// <summary>
    /// A call, <c>callvirt</c> or <c>new</c>: the receiver, not converted to the callee's type, then the arguments in order,
    /// then, for a <c>callvirt</c>, the receiver's null check, then the call and its <c>threw</c> branch, as
    /// <see cref="IrLowerer"/> dispatches one. A user-defined operator or conversion is its <c>op:</c> pure function (ticket
    /// M4-002), and an auto-property's accessor reads or writes its backing field's map (ticket M4-008), each as
    /// <see cref="IrLowerer"/> lowers it. Null for a call with no result.
    /// </summary>
    private Val? Call(CallInstruction call)
    {
        IMethodSymbol target = symbols.Method(call.Method)!;
        bool instance = call is not NewObj && !target.IsStatic;
        Val? receiver = instance ? Receiver(call.Arguments[0], target.ContainingType) : null;
        ImmutableArray<IrVar> arguments = [.. call.Arguments.Skip(instance ? 1 : 0).Select((a, i) => Value(a, target.Parameters[i].Type))];
        if (target.AssociatedSymbol is IPropertySymbol property && HeapLowerer.Inlined(property) is { } field)
        {
            return Backing(call, field, receiver, arguments);
        }

        if (call is CallVirt)
        {
            ThrowIfNull(call.Arguments[0], receiver!.Value.Var);
        }

        CallIdentity identity = CallIdentityFactory.Of(target, compilation, RenameMap.Empty, []);
        ImmutableArray<IrVar> operands = receiver is { } self ? [self.Var, .. arguments] : arguments;
        if (target.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion)
        {
            return new(Pure(PureCatalogue.UserDefined(identity), identity.RuntimeChanged, operands, Map(target.ReturnType)), target.ReturnType);
        }

        ITypeSymbol? result = call is NewObj ? target.ContainingType : target.ReturnsVoid ? null : target.ReturnType;
        IrVar? value = result is null ? null : ssa.Temp(Map(result));
        IrVar threw = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrCall(value, threw, identity, operands));
        ThrowIf(threw, PureCatalogue.AnyException);
        return value is null ? null : new(value, result!);
    }

    /// <summary>A receiver as it is, or, when it is not lowered, an opaque of the callee's type.</summary>
    private Val Receiver(ILInstruction instruction, INamedTypeSymbol declaring)
    {
        string key = IlKeys.Key(instruction, caught);
        return (IlKeys.Lowered.Contains(key), Lowerable(instruction)) switch
        {
            (false, _) => new(Opaque(instruction, key, Map(declaring))!, declaring),
            (true, false) => new(Refused(instruction, key, Map(declaring))!, declaring),
            _ => Natural(instruction, declaring),
        };
    }

    /// <summary>
    /// An auto-property's getter or setter as its backing field's map, read or written at the receiver, upcast to the
    /// field's type when it is of a derived one, or at the type's token when static; a receiver is null-checked as the
    /// IOperation lowering checks a dereferenced one.
    /// </summary>
    private Val? Backing(CallInstruction call, IFieldSymbol field, Val? receiver, ImmutableArray<IrVar> arguments)
    {
        IrVar? key = receiver is { } self
            ? Map(self.Type) == Map(field.ContainingType) ? self.Var : heap.MapRead(heap.Inputs.Cast(self.Type, field.ContainingType), self.Var, context)
            : null;
        if (key is not null)
        {
            ThrowIfNull(call.Arguments[0], key);
        }

        HeapLowerer.Access access = heap.Backing(field, key, context);
        if (arguments.IsEmpty)
        {
            return new(heap.ReadSlice(access, context), field.Type);
        }

        heap.WriteSlice(access, arguments[^1], context);
        return null;
    }

    /// <summary>An <see cref="IrPure"/> of <paramref name="function"/>, which may throw an exception of any type, and a branch on its flag.</summary>
    private IrVar Pure(string function, bool runtimeSensitive, ImmutableArray<IrVar> args, IrType result)
    {
        IrVar target = ssa.Temp(result);
        IrPureThrow flag = new(ssa.Temp(Bool), PureCatalogue.AnyException);
        ssa.Emit(context.Current, new IrPure(target, [flag], function, args) { RuntimeSensitive = runtimeSensitive });
        ThrowIf(flag.Flag, flag.ExceptionType);
        return target;
    }

    /// <summary>
    /// An opaque for <paramref name="instruction"/> and everything in it, with its key as reason and its nearest sequence
    /// point as span: a fragment with <see cref="IlFragment"/>'s fingerprint, the values it reads and a <c>threw</c> branch,
    /// as a call has, when it has one and <paramref name="fingerprint"/> allows it.
    /// </summary>
    private IrVar? Opaque(ILInstruction instruction, string key, IrType? type, bool fingerprint = true)
    {
        IrVar? target = type is null ? null : ssa.Temp(type);
        SourceSpan span = module.Pdb.Span(handle, instruction.StartILOffset) ?? bodySpan;
        if (fingerprint && fragments.Of(instruction, IsReadable) is { } fragment)
        {
            IrVar threw = ssa.Temp(Bool);
            ssa.EmitFragment(
                context.Current,
                new IrOpaque(target, key, span)
                {
                    Fingerprint = fragment.Text,
                    Reads = [.. fragment.Reads.SelectMany(Read)],
                    Threw = threw,
                },
                []);
            ThrowIf(threw, PureCatalogue.AnyException);
            return target;
        }

        ssa.Emit(context.Current, new IrOpaque(target, key, span));
        return target;
    }

    private bool IsReadable(ILVariable variable) => VariableType(variable) is not null;

    /// <summary>A fragment's read of <paramref name="variable"/>: its value, and its null shadow after it when it has one.</summary>
    private IEnumerable<IrVar> Read(ILVariable variable)
    {
        Val value = Load(variable);
        return !IsThis(variable) && Shadow(Variable(variable)) is { } shadow ? [value.Var, ssa.Load(context.Current, shadow)] : [value.Var];
    }

    /// <summary>The C# type of IL's stack slot <paramref name="type"/>: <c>long</c> for a 64-bit one, else <c>int</c>.</summary>
    private ITypeSymbol Stack(StackType type) => type == StackType.I8 ? compilation.GetSpecialType(SpecialType.System_Int64) : Int32;

    private static IrType Map(ITypeSymbol type) => TypeMapper.Map(type);

    private IrVar Const(IrValue value)
    {
        IrVar target = ssa.Temp(value.Type);
        ssa.Emit(context.Current, new IrConst(target, value));
        return target;
    }

    private IrVar Emit(IrBinaryOp op, IrVar left, IrVar right, IrType type)
    {
        IrVar target = ssa.Temp(type);
        ssa.Emit(context.Current, new IrBinary(target, op, left, right));
        return target;
    }

    private IrVar Unary(IrUnaryOp op, IrVar operand)
    {
        IrVar target = ssa.Temp(operand.Type);
        ssa.Emit(context.Current, new IrUnary(target, op, operand));
        return target;
    }

    private IrVar Resize(IrVar value, IrBitVec type, bool signed)
    {
        int width = ((IrBitVec)value.Type).Width;
        if (width == type.Width)
        {
            return value;
        }

        IrVar target = ssa.Temp(type);
        IrUnaryOp op = width switch
        {
            _ when width > type.Width => IrUnaryOp.Trunc,
            _ when signed => IrUnaryOp.SExt,
            _ => IrUnaryOp.ZExt,
        };
        ssa.Emit(context.Current, new IrUnary(target, op, value));
        return target;
    }

    /// <summary><paramref name="condition"/> <c>?</c> <paramref name="then"/> <c>:</c> <paramref name="otherwise"/>, as a branch and a join.</summary>
    private IrVar Select(IrVar condition, IrVar then, IrVar otherwise)
    {
        SsaBuilder.Variable selected = new(new IrVar($"$select{selects++.ToString(CultureInfo.InvariantCulture)}", then.Type));
        IrBlockId yes = ssa.NewBlock();
        IrBlockId no = ssa.NewBlock();
        IrBlockId join = ssa.NewBlock();
        Terminate(new IrBranch(condition, yes, no));
        ssa.Store(yes, selected, then);
        ssa.Terminate(yes, new IrGoto(join));
        ssa.Store(no, selected, otherwise);
        ssa.Terminate(no, new IrGoto(join));
        context.Current = join;
        return ssa.Load(join, selected);
    }

    /// <summary>Ends the current block, taken when <paramref name="condition"/> holds, at the shared block that throws <paramref name="exceptionType"/>.</summary>
    private void ThrowIf(IrVar condition, string exceptionType)
    {
        if (!throws.TryGetValue(exceptionType, out IrBlockId? thrown))
        {
            thrown = ssa.NewBlock();
            ssa.Terminate(thrown, new IrThrow(exceptionType, []));
            throws[exceptionType] = thrown;
        }

        IrBlockId next = ssa.NewBlock();
        ssa.Terminate(context.Current, new IrBranch(condition, thrown, next));
        context.Current = next;
    }

    private void ThrowIfOverflows(IrOverflowOp op, IrVar left, IrVar right)
    {
        IrVar overflows = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrOverflows(overflows, op, left, right));
        ThrowIf(overflows, OverflowException);
    }

    private void Jump(IrBlockId target) => Terminate(new IrGoto(target));

    /// <summary>Ends the current block; anything lowered after it, which no edge reaches, goes to a fresh block that is dropped.</summary>
    private void Terminate(IrTerminator terminator)
    {
        ssa.Terminate(context.Current, terminator);
        context.Current = ssa.NewBlock();
    }

    /// <summary>A lowered value and the C# type it has, whose sign extends it where it is narrower than IL's stack.</summary>
    private readonly record struct Val(IrVar Var, ITypeSymbol Type);
}
