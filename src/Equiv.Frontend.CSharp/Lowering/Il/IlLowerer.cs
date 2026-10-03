using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

using IlBlock = ICSharpCode.Decompiler.IL.Block;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;
using TypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// Lowers a method from its ILAst (ADR 0039; tickets P1-014 and P1-015) to the IR <see cref="IrLowerer"/> produces: the
/// same signature, parameters named as ADR 0021 names them, the same call identities, sorts and heap inputs, and the same
/// overflow, divide, null, bounds, cast and call-threw edges. Mapped here are control flow (blocks, branches, leaves,
/// <c>if</c> and <c>switch</c>), locals, constants, integral, <c>bool</c>, floating-point and <c>decimal</c> arithmetic,
/// comparisons and conversions, calls, with an auto-property's accessor its backing field's map and a <c>decimal</c> or
/// user-defined operator its <see cref="PureCatalogue"/> function, as <see cref="IrLowerer"/> makes them; the heap through
/// addresses and type tests (<c>IlLowerer.Heap.cs</c>) and exception regions (<c>IlLowerer.Exceptions.cs</c>). Every other instruction, and a
/// mapped one whose operands the IR has no type for, is an <see cref="IrOpaque"/> whose reason is its
/// <see cref="IlKeys.Key"/> and whose span is its nearest sequence point; one that is a function of the locals it reads
/// carries <see cref="IlFragment"/>'s fingerprint, as a fragment of <see cref="IrLowerer"/> carries its own (ADR 0024
/// decision 2). A run uses it under <c>--il-fallback</c>, through <see cref="IlFallback"/> (ticket P1-016).
/// </summary>
internal sealed partial class IlLowerer
{
    private const string NullReferenceException = "System.NullReferenceException";

    private static readonly IrBool Bool = new();

    /// <summary>The stack types of IL's integers the IR has: 32 and 64 bits, not native integers.</summary>
    private static readonly FrozenSet<StackType> Integral = new[] { StackType.I4, StackType.I8 }.ToFrozenSet();

    /// <summary>The stack types of IL's floating point, whose operators are <see cref="PureCatalogue"/>'s functions.</summary>
    private static readonly FrozenSet<StackType> FloatingPoint = new[] { StackType.F4, StackType.F8 }.ToFrozenSet();

    /// <summary>A floating-point comparison's stack type and sign when it is ordered: an unsigned one is IL's unordered <c>.un</c>.</summary>
    private static readonly FrozenSet<(StackType, Sign)> Ordered = new[]
    {
        (StackType.F4, Sign.None), (StackType.F4, Sign.Signed), (StackType.F8, Sign.None), (StackType.F8, Sign.Signed),
    }.ToFrozenSet();

    private static readonly FrozenDictionary<PrimitiveType, SpecialType> IntegralTargets = new Dictionary<PrimitiveType, SpecialType>
    {
        [PrimitiveType.I1] = SpecialType.System_SByte,
        [PrimitiveType.U1] = SpecialType.System_Byte,
        [PrimitiveType.I2] = SpecialType.System_Int16,
        [PrimitiveType.U2] = SpecialType.System_UInt16,
        [PrimitiveType.I4] = SpecialType.System_Int32,
        [PrimitiveType.U4] = SpecialType.System_UInt32,
        [PrimitiveType.I8] = SpecialType.System_Int64,
        [PrimitiveType.U8] = SpecialType.System_UInt64,
    }.ToFrozenDictionary();

    /// <summary>A conversion to floating point: <c>R</c> is <c>conv.r.un</c>'s, which is a <c>double</c>.</summary>
    private static readonly FrozenDictionary<PrimitiveType, SpecialType> FloatingPointTargets = new Dictionary<PrimitiveType, SpecialType>
    {
        [PrimitiveType.R4] = SpecialType.System_Single,
        [PrimitiveType.R8] = SpecialType.System_Double,
        [PrimitiveType.R] = SpecialType.System_Double,
    }.ToFrozenDictionary();

    /// <summary>A <c>decimal</c> operator method by name, as the <see cref="BinaryOperatorKind"/> the IOperation lowering applies.</summary>
    private static readonly FrozenDictionary<string, BinaryOperatorKind> DecimalOperators = new Dictionary<string, BinaryOperatorKind>(StringComparer.Ordinal)
    {
        [WellKnownMemberNames.AdditionOperatorName] = BinaryOperatorKind.Add,
        [WellKnownMemberNames.SubtractionOperatorName] = BinaryOperatorKind.Subtract,
        [WellKnownMemberNames.MultiplyOperatorName] = BinaryOperatorKind.Multiply,
        [WellKnownMemberNames.DivisionOperatorName] = BinaryOperatorKind.Divide,
        [WellKnownMemberNames.ModulusOperatorName] = BinaryOperatorKind.Remainder,
        [WellKnownMemberNames.EqualityOperatorName] = BinaryOperatorKind.Equals,
        [WellKnownMemberNames.InequalityOperatorName] = BinaryOperatorKind.NotEquals,
        [WellKnownMemberNames.LessThanOperatorName] = BinaryOperatorKind.LessThan,
        [WellKnownMemberNames.LessThanOrEqualOperatorName] = BinaryOperatorKind.LessThanOrEqual,
        [WellKnownMemberNames.GreaterThanOperatorName] = BinaryOperatorKind.GreaterThan,
        [WellKnownMemberNames.GreaterThanOrEqualOperatorName] = BinaryOperatorKind.GreaterThanOrEqual,
        [WellKnownMemberNames.IncrementOperatorName] = BinaryOperatorKind.Add,
        [WellKnownMemberNames.DecrementOperatorName] = BinaryOperatorKind.Subtract,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly SsaBuilder ssa = new();
    private readonly LoweringContext context = new([], [], handlerExit: null);
    private readonly Dictionary<ILVariable, SsaBuilder.Variable> variables = [];
    private readonly Dictionary<SsaBuilder.Variable, SsaBuilder.Variable> shadows = [];

    /// <summary>The nullness of a value known when it was made: an <c>as</c>'s failed test, a downcast's operand's.</summary>
    private readonly Dictionary<ILInstruction, IrVar> nulls = [];
    private readonly IMethodSymbol method;
    private readonly Compilation compilation;
    private readonly IlSymbols symbols;
    private readonly HeapLowerer heap;
    private readonly ExceptionLowerer exceptions;
    private readonly IlFragment fragments;
    private readonly IlAstReader.Module module;
    private readonly MethodDefinitionHandle handle;
    private readonly HashSet<ILVariable> caught;
    private readonly SourceSpan bodySpan;
    private readonly IrType? returnType;
    private readonly SideRuntime runtime;
    private readonly Func<IrBinaryOp, IrBinaryOp> mapped;

    /// <summary>The statement being lowered, whose enclosing regions decide where an exception raised in it goes.</summary>
    private ILInstruction position;
    private int selects;

    private IlLowerer(IMethodSymbol method, Compilation compilation, IlAstReader.Body body, SourceSpan bodySpan, IrType? returnType, SideRuntime runtime, Func<IrBinaryOp, IrBinaryOp> mapped)
    {
        this.method = method;
        this.runtime = runtime;
        this.mapped = mapped;
        this.compilation = compilation;
        this.bodySpan = bodySpan;
        this.returnType = returnType;
        module = body.Module!;
        position = body.Function!;
        handle = (MethodDefinitionHandle)body.Function!.Method!.MetadataToken;
        caught = IlKeys.CaughtException(body.Function);
        symbols = new IlSymbols(compilation, method);
        fragments = new IlFragment(symbols, compilation, runtime.Interval);
        // The IL lowering reaches only the members of HeapLowerer keyed by a lowered value, never those that lower an
        // IOperation, so it gives none of the callbacks those need; and only the members of ExceptionLowerer that take
        // regions of any type, never those that read a CFG.
        heap = new HeapLowerer(ssa, lower: null!, typeOf: null!, throwIfNull: null!, resolveTarget: null!, (_, condition, exceptionType) => ThrowIf(condition, exceptionType), TypeMapper.Unmapped);
        exceptions = new ExceptionLowerer(ssa, compilation: null!, cfg: null!, chains: null!, loops: null!, bodySpan, fill: null!);
    }

    /// <summary>
    /// The callee identities whose calls are rebound, each lowered as an opaque (ADR 0042; ticket P2-069), and the record of
    /// the forwarders the body's calls are resolved through (ADR 0043; ticket P2-068).
    /// </summary>
    private CallSites Sites { get; init; } = new();

    private ITypeSymbol Boolean => compilation.GetSpecialType(SpecialType.System_Boolean);

    private ITypeSymbol Int32 => compilation.GetSpecialType(SpecialType.System_Int32);

    private ITypeSymbol Object => compilation.GetSpecialType(SpecialType.System_Object);

    /// <summary>
    /// <paramref name="method"/>'s body, read from <paramref name="compilation"/>'s IL. A method with no ILAst is one
    /// whole-body opaque whose reason is <see cref="IlAstReader"/>'s. <paramref name="runtime"/> decides which calls and pure
    /// functions are runtime-sensitive, as it does for <see cref="IrLowerer"/> (ADR 0040; tickets M4-002, P2-055). A call to an identity
    /// <paramref name="sites"/> holds as rebound is an opaque, as <see cref="IrLowerer"/> makes a rebound call (ADR 0042; ticket
    /// P2-069), and each forwarder a call is resolved through is recorded there (ADR 0043; ticket P2-068). The IL has no
    /// source text, so no call site is.
    /// </summary>
    public static IrProcedure Lower(IMethodSymbol method, Compilation compilation, SideRuntime runtime, CallSites? sites = null) =>
        Lower(method, compilation, runtime, static op => op, sites);

    /// <summary>
    /// Seam for the differential soundness gate (ticket P1-017): <paramref name="mapped"/> rewrites the IR operator each
    /// integral arithmetic or comparison instruction maps to, so a test can break a mapping on purpose and show the gate
    /// catches it. The product maps every operator to itself.
    /// </summary>
    internal static IrProcedure Lower(IMethodSymbol method, Compilation compilation, SideRuntime runtime, Func<IrBinaryOp, IrBinaryOp> mapped, CallSites? sites = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(mapped);
        (ImmutableArray<IrParameter> parameters, IrType? returnType) = IrLowerer.Signature(method, TypeMapper.Unmapped);
        SourceSpan span = CSharpFrontend.ToSourceSpan(method.DeclaringSyntaxReferences is [var syntax, ..] ? syntax.GetSyntax().GetLocation() : method.Locations[0]);
        IlAstReader.Body body = IlAstReader.Read(method, compilation);
        return body.Failure is { } failure
            ? IrLowerer.Opaque(RoslynIdentity.Of(method, RenameMap.Empty), parameters, returnType, failure, [span])
            : new IlLowerer(method, compilation, body, span, returnType, runtime, mapped) { Sites = sites ?? new CallSites() }.Procedure(parameters, body.Function!);
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
    /// A container: its blocks, entered at its entry point; a <c>leave</c> of it goes on to what follows it, or to
    /// <paramref name="exit"/> when given (a <c>finally</c> copy's continuation), except that one leaving the function returns.
    /// </summary>
    private void Container(BlockContainer container, IrBlockId? exit = null)
    {
        scope.Exits[container] = exit ?? ssa.NewBlock();
        foreach (IlBlock block in container.Blocks)
        {
            scope.Blocks[block] = ssa.NewBlock();
        }

        Jump(scope.Blocks[container.EntryPoint]);
        foreach (IlBlock block in container.Blocks)
        {
            context.Current = scope.Blocks[block];
            Sequence(block);
        }

        context.Current = scope.Exits[container];
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
        ILInstruction outer = position;
        position = instruction;
        Execute(instruction);
        position = outer;
    }

    private void Execute(ILInstruction instruction)
    {
        string key = IlKeys.Key(instruction, caught);
        bool unreachableEnd = instruction.HasFlag(InstructionFlags.EndPointUnreachable);
        if (!IlKeys.Lowered.Contains(key) || (instruction is TryInstruction && !Lowerable(instruction)))
        {
            // A throw, or a region every path of which leaves: its opaque stands for where the method goes next.
            IrVar? opaque = Opaque(instruction, key, unreachableEnd ? returnType : null);
            Finish(unreachableEnd, opaque);
            return;
        }

        if (!Lowerable(instruction))
        {
            Finish(unreachableEnd, Refused(instruction, key, unreachableEnd ? returnType : null));
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
                Jump(Unwind(Crossed(branch, branch.TargetBlock.Parent!), scope.Blocks[branch.TargetBlock]));
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
            case TryInstruction region:
                Region(region);
                return;
            case Throw thrown:
                Throw(thrown);
                return;
            case CallInstruction call:
                _ = Call(call);
                return;
            case var address when IsAddress(address):
                _ = Address(address);
                return;
            default:
                _ = Natural(instruction, Object);
                return;
        }
    }

    /// <summary>After an opaque statement that cannot complete, its value is where the method goes: it returns it.</summary>
    private void Finish(bool unreachableEnd, IrVar? opaque)
    {
        if (unreachableEnd)
        {
            Terminate(new IrReturn(opaque, []));
        }
    }

    /// <summary>
    /// Whether an instruction whose key is mapped is lowered: the IR has its operands' types. Decided before anything of it
    /// is emitted.
    /// </summary>
    private bool Lowerable(ILInstruction instruction) => instruction switch
    {
        _ when IsAddress(instruction) => Addressable(instruction),
        LdLoc load => VariableType(load.Variable) is not null,
        StLoc store when IsAddressSlot(store.Variable) => Addressable(store.Value),
        StLoc store => VariableType(store.Variable) is not null,
        LdObj load => Addressable(load.Target),
        StObj store => Addressable(store.Target),
        BinaryNumericInstruction binary => Integral.IsSupersetOf([binary.LeftInputType, binary.RightInputType]) || FloatingPoint.Contains(binary.LeftInputType),
        Comp comparison => Integral.Contains(comparison.InputType)
            || Ordered.Contains((comparison.InputType, comparison.Sign))
            || (comparison.InputType == StackType.Obj && comparison.Right is LdNull),
        Conv conversion => (Integral.Contains(InputType(conversion)) && (IntegralTargets.ContainsKey(conversion.TargetType) || FloatingPointTargets.ContainsKey(conversion.TargetType)))
            || (FloatingPoint.Contains(conversion.InputType) && (IntegralTargets.ContainsKey(conversion.TargetType) || FloatingPointTargets.ContainsKey(conversion.TargetType))),
        CallInstruction call => Callable(call) && (!IsTypeOf(call) || symbols.Type(((LdTypeToken)call.Arguments[0]).Type) is { TypeKind: not TypeKind.TypeParameter }),
        Throw thrown => Callable((NewObj)thrown.Argument),
        DefaultValue value => symbols.Type(value.Type) is { } type && TypeMapper.Default(type, TypeMapper.Unmapped) is not null,
        _ => Typed(instruction),
    };

    /// <summary>Whether the heap, type-test, function and region instructions have the types the IR needs.</summary>
    private bool Typed(ILInstruction instruction) => instruction switch
    {
        NewArr array => array.Indices[0].ResultType == StackType.I4 && symbols.Type(array.Type) is { } element && element is not IArrayTypeSymbol && TypeMapper.Default(element, TypeMapper.Unmapped) is not null,
        Box box => symbols.Type(box.Type) is { TypeKind: not TypeKind.TypeParameter, OriginalDefinition.SpecialType: not SpecialType.System_Nullable_T },
        IsInst test => IsTestable(test.Type),
        CastClass test => IsTestable(test.Type),
        LdTypeToken => false,
        LdFtn function => symbols.Method(function.Method) is not null,
        LdVirtFtn function => symbols.Method(function.Method) is not null,
        TryCatch region => region.Handlers.All(h => h.Filter is LdcI4 { Value: 1 } or BlockContainer && symbols.Type(h.Variable.Type) is not null),
        _ => true,
    };

    /// <summary>Whether a type test's type is one M4-005's <c>istype</c> takes: a reference type that is not a type parameter.</summary>
    private bool IsTestable(IType type) => symbols.Type(type) is { IsReferenceType: true, TypeKind: not TypeKind.TypeParameter };

    /// <summary>
    /// A mapped instruction the IR has no types for, such as a comparison of references or a call with a <c>ref</c>
    /// argument it cannot write: its operands are lowered, each unmapped one an opaque of its own key, and then it is an
    /// opaque with its key.
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
    /// A constant, which has none, takes <paramref name="hint"/>'s, and so does a box, when <paramref name="hint"/> is the
    /// reference type it is boxed to.
    /// </summary>
    private Val Natural(ILInstruction instruction, ITypeSymbol hint) => instruction switch
    {
        _ when IsAddress(instruction) => new(Opaque(instruction, IlKeys.Key(instruction, caught), Map(hint), fingerprint: false)!, hint),
        LdLoc load => Load(load.Variable),
        LdStr text => Literal(SpecialType.System_String, text.Value),
        LdcF4 constant => Literal(SpecialType.System_Single, constant.Value),
        LdcF8 constant => Literal(SpecialType.System_Double, constant.Value),
        LdcDecimal constant => Literal(SpecialType.System_Decimal, constant.Value),
        DefaultValue value => Default(symbols.Type(value.Type)!),
        BinaryNumericInstruction binary => Binary(binary),
        Comp comparison => new(Compare(comparison), Boolean),
        Conv conversion => Convert(conversion),
        CallInstruction call => Call(call)!.Value,
        IfInstruction choice => new(Conditional(choice, hint), hint),
        _ => Heap(instruction, hint),
    };

    private Val Literal(SpecialType type, object value)
    {
        ITypeSymbol symbol = compilation.GetSpecialType(type);
        return new(Const(TypeMapper.Constant(symbol, value)), symbol);
    }

    private Val Default(ITypeSymbol type) => new(Const(TypeMapper.Default(type, TypeMapper.Unmapped)!), type);

    /// <summary>
    /// <paramref name="value"/> as a value of <paramref name="type"/>: a bitvector extended by its own type's sign or
    /// truncated, as IL's stack does; a bitvector as Bool is whether it is not zero, and Bool as a bitvector 1 or 0; a
    /// value of one sort as another is its <c>cast</c> map read (ticket M3-010), since IL that verifies converts between
    /// two types with no instruction only by an implicit reference conversion. Null, with nothing emitted, between a
    /// bitvector and a sort, such as an enum's value as an integer.
    /// </summary>
    private IrVar? Coerce(Val value, ITypeSymbol type) => (value.Var.Type, Map(type)) switch
    {
        var (from, to) when from == to => value.Var,
        (IrBitVec, IrBitVec to) => Resize(value.Var, to, TypeMapper.IsSigned(value.Type)),
        (IrBitVec from, IrBool) => Emit(IrBinaryOp.Ne, value.Var, Const(new IrBitVecValue(from.Width, 0)), Bool),
        (IrBool, IrBitVec to) => Select(value.Var, Const(new IrBitVecValue(to.Width, 1)), Const(new IrBitVecValue(to.Width, 0))),
        (IrSort, IrSort) => heap.MapRead(heap.Inputs.Cast(value.Type, type), value.Var, context),
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

    /// <summary>
    /// A store to a local; to a stack slot that holds an address, the place it names, evaluated once; to a variable that
    /// holds a caught exception, which is a copy of another, nothing, as the IR has no value for it.
    /// </summary>
    private void Store(StLoc store)
    {
        if (IsAddressSlot(store.Variable))
        {
            slots[store.Variable] = Address(store.Value);
            return;
        }

        if (caught.Contains(store.Variable))
        {
            return;
        }

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
            declared = variable.Kind == VariableKind.Local && variable.HasGeneratedName ? new SsaBuilder.Variable(template) : Shadowed(template, type);
            variables[variable] = declared;
            if (variable.Kind == VariableKind.Local && TypeMapper.Default(type, TypeMapper.Unmapped) is { } initial)
            {
                // A local starts at its default, stored ahead of everything in the entry as the heap's inputs are. C#'s
                // definite assignment leaves only an infeasible path, such as a branch the optimiser made constant, reading
                // a local before a store, so the default is what `.locals init` gives it and never changes a feasible run.
                Initialize(declared, initial);
                if (Shadow(declared) is { } shadow)
                {
                    Initialize(shadow, new IrBoolValue(Value: true));
                }
            }
        }

        return declared;
    }

    private void Initialize(SsaBuilder.Variable variable, IrValue value)
    {
        IrVar initial = ssa.Temp(value.Type);
        ssa.Emit(new IrBlockId(0), new IrConst(initial, value));
        ssa.StoreFirst(new IrBlockId(0), variable, initial);
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
    /// Whether <paramref name="source"/>'s value is null, or null when it provably is not: a <c>new</c>, a new array,
    /// <c>typeof</c> and <c>this</c> are not, <c>null</c> is, a variable carries its shadow, an <c>as</c> or a downcast
    /// its test's, and anything else asks the <c>null.&lt;Sort&gt;</c> map.
    /// </summary>
    private IrVar? Nullness(ILInstruction source, IrVar value) => source switch
    {
        _ when nulls.TryGetValue(source, out IrVar? known) => known,
        NewObj or NewArr => null,
        CallInstruction call when IsTypeOf(call) => null,
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
            ThrowIf(isNull, NullReferenceException);
        }
    }

    /// <summary>
    /// A <c>leave</c>: of a filter's container, the filter's verdict; of the function's container, a return of its value
    /// as the return type; else a jump to what follows the container. Each <c>finally</c> it leaves runs on the way, after
    /// its value is evaluated.
    /// </summary>
    private void Leave(Leave leave)
    {
        if (scope.Filter is { } filter && leave.TargetContainer == filter.Container)
        {
            Terminate(new IrBranch(Value(leave.Value, Boolean), filter.Taken, filter.Declined));
            return;
        }

        ImmutableArray<ILInstruction> crossed = Crossed(leave, leave.TargetContainer);
        if (leave.IsLeavingFunction)
        {
            IrVar? value = returnType is null ? null : Value(leave.Value, ReturnType());
            IrBlockId exit = ssa.NewBlock();
            ssa.Terminate(exit, new IrReturn(value, []));
            Jump(Unwind(crossed, exit));
        }
        else
        {
            Jump(Unwind(crossed, scope.Exits[leave.TargetContainer]));
        }
    }

    /// <summary>
    /// The type <see cref="IrLowerer.Signature"/> returns, asked only when it returns one: an async method's task result,
    /// the one type argument of its task-like type, else the return type.
    /// </summary>
    private ITypeSymbol ReturnType() =>
        method.IsAsync && !method.IsIterator ? ((INamedTypeSymbol)method.ReturnType).TypeArguments[0] : method.ReturnType;

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
    /// An <c>if</c> with a value, as ILSpy inlines a <c>&amp;&amp;</c> or <c>||</c> into an expression: a branch, each arm
    /// the value of <paramref name="type"/> it yields, and a join.
    /// </summary>
    private IrVar Conditional(IfInstruction choice, ITypeSymbol type)
    {
        IrVar condition = Value(choice.Condition, Boolean);
        SsaBuilder.Variable selected = new(new IrVar($"$select{selects++.ToString(CultureInfo.InvariantCulture)}", Map(type)));
        IrBlockId then = ssa.NewBlock();
        IrBlockId otherwise = ssa.NewBlock();
        IrBlockId join = ssa.NewBlock();
        Terminate(new IrBranch(condition, then, otherwise));
        context.Current = then;
        ssa.Store(context.Current, selected, Value(choice.TrueInst, type));
        Jump(join);
        context.Current = otherwise;
        ssa.Store(context.Current, selected, Value(choice.FalseInst, type));
        Jump(join);
        context.Current = join;
        return ssa.Load(join, selected);
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
    /// An integral operator, or <c>&amp;</c>, <c>|</c> or <c>^</c> of two Bool values; its <see cref="Sign"/>, not its
    /// operands' types, decides a signed or unsigned division, remainder, right shift and overflow check. On floating
    /// point it is <see cref="PureCatalogue"/>'s function, and IL's <c>neg</c> is <c>neg</c>.
    /// </summary>
    private Val Binary(BinaryNumericInstruction binary)
    {
        if (IsFloatingPoint(binary))
        {
            ITypeSymbol type = Stack(binary.LeftInputType);
            return new(
                IsNegation(binary)
                    ? Apply(PureCatalogue.Negation(type)!, [Value(binary.Right, type)], type)
                    : Apply(PureCatalogue.Binary(Kind(binary.Operator), type, type)!, [Value(binary.Left, type), Value(binary.Right, type)], type),
                type);
        }

        bool signed = binary.Sign == Sign.Signed;
        IrBinaryOp op = mapped(Operation(binary.Operator, signed));
        if (op is IrBinaryOp.And or IrBinaryOp.Or or IrBinaryOp.Xor && IsBool(binary.Left) && IsBool(binary.Right))
        {
            return new(Emit(op, Value(binary.Left, Boolean), Value(binary.Right, Boolean), Bool), Boolean);
        }

        ITypeSymbol operands = Stack(binary.LeftInputType);
        IrVar left = Value(binary.Left, operands);
        IrVar right = Value(binary.Right, OperatorMapper.IsShift(op) ? Stack(binary.RightInputType) : operands);
        IrVar result = op switch
        {
            IrBinaryOp.SDiv or IrBinaryOp.SRem or IrBinaryOp.UDiv or IrBinaryOp.URem => Divide(op, left, right, signed),
            _ when OperatorMapper.IsShift(op) => Shift(op, left, right),
            _ => Arithmetic(op, left, right, binary.CheckForOverflow, signed),
        };
        return new(result, operands);
    }

    /// <summary>
    /// Whether a binary operator is on floating point: IL has only <c>+ - * / %</c> on it, of two operands of one width.
    /// </summary>
    private static bool IsFloatingPoint(BinaryNumericInstruction binary) => FloatingPoint.Contains(binary.LeftInputType);

    /// <summary>
    /// Whether <paramref name="binary"/> is IL's <c>neg</c>, which ILSpy reads as a subtraction from a <c>0.0</c> it makes
    /// itself, with no IL range, where C#'s <c>0.0 - x</c> has its own: the two differ when <c>x</c> is <c>0.0</c>.
    /// </summary>
    private static bool IsNegation(BinaryNumericInstruction binary) =>
        binary.Operator == BinaryNumericOperator.Sub && binary.Left is LdcF4 or LdcF8 && binary.Left.ILRangeIsEmpty;

    /// <summary>The C# operator of a floating-point <paramref name="op"/>; <see cref="BinaryNumericOperator.Rem"/> is the last.</summary>
    private static BinaryOperatorKind Kind(BinaryNumericOperator op) => op switch
    {
        BinaryNumericOperator.Add => BinaryOperatorKind.Add,
        BinaryNumericOperator.Sub => BinaryOperatorKind.Subtract,
        BinaryNumericOperator.Mul => BinaryOperatorKind.Multiply,
        BinaryNumericOperator.Div => BinaryOperatorKind.Divide,
        _ => BinaryOperatorKind.Remainder,
    };

    /// <summary>The C# comparison of <paramref name="kind"/>; <see cref="ComparisonKind.GreaterThanOrEqual"/> is the last.</summary>
    private static BinaryOperatorKind Kind(ComparisonKind kind) => kind switch
    {
        ComparisonKind.Equality => BinaryOperatorKind.Equals,
        ComparisonKind.Inequality => BinaryOperatorKind.NotEquals,
        ComparisonKind.LessThan => BinaryOperatorKind.LessThan,
        ComparisonKind.LessThanOrEqual => BinaryOperatorKind.LessThanOrEqual,
        ComparisonKind.GreaterThan => BinaryOperatorKind.GreaterThan,
        _ => BinaryOperatorKind.GreaterThanOrEqual,
    };

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
        ThrowIf(Emit(IrBinaryOp.Eq, right, Const(new IrBitVecValue(((IrBitVec)right.Type).Width, 0)), Bool), PureCatalogue.DivideByZero);
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
        if (isChecked)
        {
            // Only `add`, `sub` and `mul` check for overflow.
            ThrowIfOverflows(OperatorMapper.Overflow(op, signed)!.Value, left, right);
        }

        return Emit(op, left, right, left.Type);
    }

    /// <summary>
    /// An integral comparison by its <see cref="Sign"/>, an equality of two values one of which is Bool, a floating-point
    /// comparison's <see cref="PureCatalogue"/> function (ILSpy reads IL's unordered comparisons as the negation of an
    /// ordered one), or a reference against <c>null</c>, which ILSpy puts on the right, reading its null shadow.
    /// </summary>
    private IrVar Compare(Comp comparison)
    {
        if (comparison.InputType == StackType.Obj)
        {
            IrVar isNull = NullFlag(comparison.Left, Receiver(comparison.Left, Object).Var);
            return comparison.Kind == ComparisonKind.Equality ? isNull : Not(isNull);
        }

        if (FloatingPoint.Contains(comparison.InputType))
        {
            ITypeSymbol type = Stack(comparison.InputType);
            return Apply(PureCatalogue.Binary(Kind(comparison.Kind), type, type)!, [Value(comparison.Left, type), Value(comparison.Right, type)], Boolean);
        }

        if (comparison is { Kind: ComparisonKind.Equality or ComparisonKind.Inequality, InputType: StackType.I4 } && (IsBool(comparison.Left) || IsBool(comparison.Right)))
        {
            return Emit(mapped(comparison.Kind == ComparisonKind.Equality ? IrBinaryOp.Eq : IrBinaryOp.Ne), Value(comparison.Left, Boolean), Value(comparison.Right, Boolean), Bool);
        }

        ITypeSymbol operands = Stack(comparison.InputType);
        IrVar left = Value(comparison.Left, operands);
        IrVar right = Value(comparison.Right, operands);
        return Emit(mapped(Comparison(comparison.Kind, unsigned: comparison.Sign == Sign.Unsigned)), left, right, Bool);
    }

    /// <summary>The IR comparison of <paramref name="kind"/> on integers; <see cref="ComparisonKind.GreaterThanOrEqual"/> is the last.</summary>
    private static IrBinaryOp Comparison(ComparisonKind kind, bool unsigned) => kind switch
    {
        ComparisonKind.Equality => IrBinaryOp.Eq,
        ComparisonKind.Inequality => IrBinaryOp.Ne,
        ComparisonKind.LessThan => unsigned ? IrBinaryOp.Ult : IrBinaryOp.Slt,
        ComparisonKind.LessThanOrEqual => unsigned ? IrBinaryOp.Ule : IrBinaryOp.Sle,
        ComparisonKind.GreaterThan => unsigned ? IrBinaryOp.Ugt : IrBinaryOp.Sgt,
        _ => unsigned ? IrBinaryOp.Uge : IrBinaryOp.Sge,
    };

    /// <summary>Whether <paramref name="instruction"/>'s value is a C# <c>bool</c>, which the IR keeps as Bool although IL's stack has an int.</summary>
    private bool IsBool(ILInstruction instruction) => instruction switch
    {
        Comp => true,
        LdLoc load => SymbolEqualityComparer.Default.Equals(VariableType(load.Variable), Boolean),
        LdObj load => SymbolEqualityComparer.Default.Equals(symbols.Type(load.Type), Boolean),
        CallInstruction call => symbols.Method(call.Method) is { } target && SymbolEqualityComparer.Default.Equals(target.ReturnType, Boolean),
        BinaryNumericInstruction { Operator: BinaryNumericOperator.BitAnd or BinaryNumericOperator.BitOr or BinaryNumericOperator.BitXor } binary =>
            IsBool(binary.Left) && IsBool(binary.Right),
        _ => false,
    };

    /// <summary>
    /// A conversion: between integral types, extension by its kind, truncation, or a change of sign alone, a checked one
    /// throwing when the value does not fit, its input read with the conversion's input sign; to or from floating point,
    /// <see cref="PureCatalogue"/>'s <c>conv</c> function, a change to the same precision being the value itself.
    /// </summary>
    private Val Convert(Conv conversion)
    {
        ITypeSymbol target = compilation.GetSpecialType(IntegralTargets.TryGetValue(conversion.TargetType, out SpecialType integral) ? integral : FloatingPointTargets[conversion.TargetType]);
        if (FloatingPoint.Contains(conversion.InputType) || FloatingPointTargets.ContainsKey(conversion.TargetType))
        {
            ITypeSymbol from = Numeric(conversion);
            IrVar operand = Value(conversion.Argument, from);
            return new(
                from.SpecialType == target.SpecialType ? operand : Apply(PureCatalogue.Conversion(from, target)!, [operand], target, conversion.CheckForOverflow),
                target);
        }

        IrVar value = Value(conversion.Argument, Stack(InputType(conversion)));
        IrVar result = Resize(value, (IrBitVec)Map(target), conversion.Kind == ConversionKind.SignExtend);
        if (conversion.CheckForOverflow)
        {
            ThrowIfItDoesNotFit(value, result, conversion.InputSign == Sign.Signed, TypeMapper.IsSigned(target));
        }

        return new(result, target);
    }

    /// <summary>
    /// The C# type a numeric conversion converts from: <c>float</c> or <c>double</c>, or the integer of its stack type
    /// and input sign, <c>conv.r.un</c>'s being unsigned.
    /// </summary>
    private ITypeSymbol Numeric(Conv conversion) => compilation.GetSpecialType((conversion.InputType, conversion.InputSign) switch
    {
        (StackType.F4, _) => SpecialType.System_Single,
        (StackType.F8, _) => SpecialType.System_Double,
        (StackType.I4, Sign.Unsigned) => SpecialType.System_UInt32,
        (StackType.I4, _) => SpecialType.System_Int32,
        (_, Sign.Unsigned) => SpecialType.System_UInt64,
        _ => SpecialType.System_Int64,
    });

    /// <summary>
    /// A conversion's input stack type, except that an array's length, which ILSpy types as a native integer, is an
    /// <c>int</c>, as C#'s <c>Length</c> is: no array is longer.
    /// </summary>
    private static StackType InputType(Conv conversion) =>
        conversion.InputType == StackType.I && conversion.Argument is LdLen ? StackType.I4 : conversion.InputType;

    /// <summary>Throws when <paramref name="result"/> does not round-trip to <paramref name="value"/>, or when the signed side of a signedness change is negative.</summary>
    private void ThrowIfItDoesNotFit(IrVar value, IrVar result, bool fromSigned, bool toSigned)
    {
        IrVar lost = Emit(IrBinaryOp.Ne, Resize(result, (IrBitVec)value.Type, toSigned), value, Bool);
        if (fromSigned != toSigned)
        {
            IrVar signedSide = fromSigned ? value : result;
            lost = Emit(IrBinaryOp.Or, lost, Emit(IrBinaryOp.Slt, signedSide, Const(new IrBitVecValue(((IrBitVec)signedSide.Type).Width, 0)), Bool), Bool);
        }

        ThrowIf(lost, PureCatalogue.Overflow);
    }

    /// <summary>
    /// A call, <c>callvirt</c> or <c>new</c>: the receiver, not converted to the callee's type unless that is an interface
    /// (the conversion C# spells), then the arguments in order, a <c>ref</c> one read after all of them and an <c>out</c>
    /// one passing nothing; then, for a <c>callvirt</c> on a reference, the receiver's null check; then the call, each
    /// <c>ref</c> or <c>out</c> variable written from its output (ticket M4-003), and its <c>threw</c> branch, as
    /// <see cref="IrLowerer"/> dispatches one. <c>typeof(T)</c> is the shared <c>typeof</c> input (ticket P2-002); an
    /// operator of <c>decimal</c> or a user-defined one is its <see cref="PureCatalogue"/> function (ticket M4-002), and an
    /// auto-property's accessor reads or writes its backing field's map (ticket M4-008), each as <see cref="IrLowerer"/>
    /// lowers it. A struct's constructor is only ever a <c>newobj</c> here: ILSpy reads one called on a local's address as
    /// the local's store of a <c>newobj</c>, and Roslyn stores one into a field or element. Null for a call with no result.
    /// A call to a forwarder is the same call to its target (<see cref="Callee"/>; ADR 0043).
    /// A call to an identity the pair's other side binds differently at the same source text is <see cref="Rebound"/> (ADR 0042).
    /// </summary>
    private Val? Call(CallInstruction call)
    {
        IMethodSymbol target = symbols.Method(call.Method)!;
        if (IsTypeOf(call))
        {
            return new(heap.Inputs.TypeOf(symbols.Type(((LdTypeToken)call.Arguments[0]).Type)!, target.ReturnType), target.ReturnType);
        }

        bool instance = call is not NewObj && !target.IsStatic;
        Val? receiver = instance ? Interface(Receiver(call.Arguments[0], target.ContainingType), target.ContainingType) : null;
        if (call is CallVirt)
        {
            target = Overriding(target, receiver!.Value.Type);
        }

        (ImmutableArray<IrVar> arguments, ImmutableArray<Place> written) = Arguments(call, target, instance ? 1 : 0);
        if (target.AssociatedSymbol is IPropertySymbol property && HeapLowerer.Inlined(property) is { } field)
        {
            return Backing(call, field, receiver, arguments);
        }

        bool isOperator = target.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion;
        if (isOperator && target.ContainingType.SpecialType == SpecialType.System_Decimal)
        {
            return Decimal(target, arguments);
        }

        if (call is CallVirt && !receiver!.Value.Type.IsValueType)
        {
            ThrowIfNull(call.Arguments[0], receiver.Value.Var);
        }

        (CallIdentity identity, bool closed) = Callee(target);
        ImmutableArray<IrVar> operands = receiver is { } self ? [self.Var, .. arguments] : arguments;
        return (isOperator, Sites.IsRebound(identity)) switch
        {
            (true, _) => new(Pure(PureCatalogue.UserDefined(identity), [PureCatalogue.AnyException], identity.RuntimeChanged, operands, Map(target.ReturnType)), target.ReturnType),
            (_, true) => Rebound(call, written, Result(call, target)),
            _ => Invoke(identity, operands, written, Result(call, target), closed),
        };
    }

    /// <summary>
    /// What a call to <paramref name="target"/> calls, and whether that call is closed (ADR 0041): <paramref name="target"/>
    /// itself, or, when it is a forwarder, the target of its chain, as <see cref="IrLowerer"/> resolves one (ADR 0043; ticket
    /// P2-068), unless the pair's sides do not agree on it. The forwarder's parameters and result are its target's, so the
    /// operands and the result's type stay as read.
    /// </summary>
    private (CallIdentity Identity, bool Closed) Callee(IMethodSymbol target)
    {
        CallIdentity identity = CallIdentityFactory.Of(target, compilation, RenameMap.Empty, [], runtime.Interval);
        if (Sites.KeptForwarders.Contains(identity.Value) || Forwarders.Resolve(target, compilation) is not { } resolved)
        {
            return (identity, ClosedCalls.IsClosed(target));
        }

        CallIdentity forwarded = CallIdentityFactory.Of(resolved.Target, resolved.Compilation, RenameMap.Empty, [], runtime.Interval);
        Sites.Forwarded(identity, forwarded);
        return (forwarded, ClosedCalls.IsClosed(resolved.Target));
    }

    /// <summary>
    /// A rebound call (ADR 0042; ticket P2-069), as <see cref="IrLowerer"/> makes one: its result, if any, and each
    /// <c>ref</c> or <c>out</c> variable in <paramref name="written"/> an opaque with reason
    /// <see cref="ReboundCall.OpaqueReason"/> and no fingerprint, and no <c>threw</c> branch.
    /// </summary>
    private Val? Rebound(CallInstruction call, ImmutableArray<Place> written, ITypeSymbol? result)
    {
        IrVar? value = Opaque(call, ReboundCall.OpaqueReason, result is null ? null : Map(result), fingerprint: false);
        foreach (Place place in written)
        {
            WriteUnknown((VariablePlace)place, Opaque(call, ReboundCall.OpaqueReason, Map(place.Type), fingerprint: false)!);
        }

        return value is null ? null : new(value, result!);
    }

    /// <summary>What a call yields: a <c>new</c> its type's new object, else the callee's result, if any.</summary>
    private static ITypeSymbol? Result(CallInstruction call, IMethodSymbol target) => (call, target.ReturnsVoid) switch
    {
        (NewObj, _) => target.ContainingType,
        (_, true) => null,
        _ => target.ReturnType,
    };

    /// <summary>
    /// The <see cref="IrCall"/> itself: its result, if any, of <paramref name="result"/>, each <c>ref</c> or <c>out</c>
    /// variable in <paramref name="written"/> stored from its output, and then its <c>threw</c> branch. A
    /// <paramref name="closed"/> call reaches no heap map (ADR 0041), so it gets no heap pairs.
    /// </summary>
    private Val? Invoke(CallIdentity identity, ImmutableArray<IrVar> operands, ImmutableArray<Place> written, ITypeSymbol? result, bool closed)
    {
        IrVar? value = result is null ? null : ssa.Temp(Map(result));
        IrVar threw = ssa.Temp(Bool);
        ImmutableArray<IrVar> outputs = [.. written.Select(p => ssa.Temp(Map(p.Type)))];
        ssa.Emit(context.Current, new IrCall(value, threw, identity, operands) { RefOuts = outputs, Closed = closed });
        foreach ((Place place, IrVar output) in written.Zip(outputs))
        {
            WriteUnknown((VariablePlace)place, output);
        }

        ThrowIf(threw, PureCatalogue.AnyException);
        return value is null ? null : new(value, result!);
    }

    /// <summary>
    /// The override of <paramref name="target"/> nearest <paramref name="receiver"/>'s static type, which is the method C#
    /// binds and the IOperation lowering names; a <c>callvirt</c> names the method it overrides. <paramref name="target"/>
    /// itself when no type from the receiver's up overrides it.
    /// </summary>
    private static IMethodSymbol Overriding(IMethodSymbol target, ITypeSymbol receiver)
    {
        for (INamedTypeSymbol? type = receiver as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(target.Name).OfType<IMethodSymbol>().FirstOrDefault(m => Overrides(m, target)) is { } candidate)
            {
                return candidate.IsGenericMethod ? candidate.Construct([.. target.TypeArguments]) : candidate;
            }
        }

        return target;
    }

    private static bool Overrides(IMethodSymbol method, IMethodSymbol target)
    {
        for (IMethodSymbol? overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, target.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A call's arguments after its receiver: each as its parameter's type, an <c>in</c> one read from its address, a
    /// <c>ref</c> one read after every other argument, since the callee reads it through the reference, and an <c>out</c>
    /// one passing nothing; and the places a <c>ref</c> or <c>out</c> argument names, which the call writes, in order.
    /// </summary>
    private (ImmutableArray<IrVar> Arguments, ImmutableArray<Place> Written) Arguments(CallInstruction call, IMethodSymbol target, int first)
    {
        IrVar?[] passed = new IrVar?[target.Parameters.Length];
        Place?[] written = new Place?[target.Parameters.Length];
        for (int i = 0; i < passed.Length; i++)
        {
            ILInstruction argument = call.Arguments[first + i];
            if (IsWritten(target.Parameters[i]))
            {
                written[i] = Address(argument);
            }
            else
            {
                passed[i] = IsAddress(argument) ? Read(Address(argument)).Var : Value(argument, target.Parameters[i].Type);
            }
        }

        for (int i = 0; i < passed.Length; i++)
        {
            if (target.Parameters[i].RefKind == RefKind.Ref)
            {
                passed[i] = Read(written[i]!).Var;
            }
        }

        return ([.. passed.OfType<IrVar>()], [.. written.OfType<Place>()]);
    }

    private static bool IsWritten(IParameterSymbol parameter) => parameter.RefKind is RefKind.Ref or RefKind.Out;

    /// <summary>
    /// Whether a call is lowered: its callee resolves and returns by value; a <c>constrained.</c> prefix names a type that
    /// is not a type parameter; a receiver or <c>in</c> argument given by address is addressable; and each <c>ref</c> or
    /// <c>out</c> argument is the address of a variable, no two of the same one, since which write lands last is the
    /// callee's order, which the call's outputs do not model (the IOperation lowering's rule, ticket M4-003).
    /// </summary>
    private bool Callable(CallInstruction call)
    {
        if (symbols.Method(call.Method) is not { RefKind: RefKind.None } target
            || (call.ConstrainedTo is { } constrained && symbols.Type(constrained) is null or ITypeParameterSymbol)
            || !call.Arguments.All(a => !IsAddress(a) || Addressable(a)))
        {
            return false;
        }

        int first = call.Arguments.Count - target.Parameters.Length;
        ImmutableArray<ILInstruction> written = [.. target.Parameters.Where(IsWritten).Select(p => call.Arguments[first + p.Ordinal])];
        HashSet<ILVariable> distinct = [.. written.OfType<IInstructionWithVariableOperand>().Select(static a => a.Variable)];
        return written.All(static a => a is LdLoca or LdLoc { Variable.Kind: VariableKind.Parameter }) && distinct.Count == written.Length;
    }

    /// <summary>
    /// Whether <paramref name="call"/> is <c>typeof(T)</c>: a call of a type token alone, which C# only passes to
    /// <c>Type.GetTypeFromHandle</c> (a <c>TypeHandle</c> is read from the type it returns).
    /// </summary>
    private static bool IsTypeOf(CallInstruction call) => call.Arguments.Count == 1 && call.Arguments[0] is LdTypeToken;

    /// <summary>
    /// A receiver as it is, read from its address when it is given by one, or, when it is not lowered, an opaque of
    /// <paramref name="type"/>.
    /// </summary>
    private Val Receiver(ILInstruction instruction, ITypeSymbol type)
    {
        string key = IlKeys.Key(instruction, caught);
        return (IlKeys.Lowered.Contains(key), Lowerable(instruction)) switch
        {
            (false, _) => new(Opaque(instruction, key, Map(type))!, type),
            (true, false) => new(Refused(instruction, key, Map(type))!, type),
            _ when IsAddress(instruction) => Read(Address(instruction)),
            _ => Natural(instruction, type),
        };
    }

    /// <summary>
    /// A reference receiver of an interface's method as a value of that interface, its <c>cast</c> map read, as the
    /// conversion C# spells for it (a <c>using</c>'s <c>IDisposable</c>, an explicit implementation) reads it.
    /// </summary>
    private Val Interface(Val receiver, INamedTypeSymbol declaring) =>
        declaring.TypeKind == TypeKind.Interface && receiver.Type.IsReferenceType && Map(receiver.Type) != Map(declaring)
            ? new(heap.MapRead(heap.Inputs.Cast(receiver.Type, declaring), receiver.Var, context), declaring)
            : receiver;

    /// <summary>
    /// An auto-property's getter or setter as its backing field's map, read or written at the receiver, upcast to the
    /// field's type when it is of a derived one, or at the type's token when static; a receiver is null-checked as the
    /// IOperation lowering checks a dereferenced one. A struct's is its field of the struct at the receiver's address, so
    /// a setter makes a new struct there.
    /// </summary>
    private Val? Backing(CallInstruction call, IFieldSymbol field, Val? receiver, ImmutableArray<IrVar> arguments)
    {
        if (receiver is { Type.IsValueType: true } instance)
        {
            MemberPlace member = new(places[call.Arguments[0]], field);
            if (arguments.IsEmpty)
            {
                return MemberOf(member, instance);
            }

            Write(member, arguments[^1], call);
            return null;
        }

        IrVar? key = receiver is { } self ? Upcast(self, field.ContainingType) : null;
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

    /// <summary><paramref name="value"/> as a key of a map of <paramref name="type"/>'s members: itself, or its <c>cast</c> read when it is of a derived type.</summary>
    private IrVar Upcast(Val value, INamedTypeSymbol type) =>
        Map(value.Type) == Map(type) ? value.Var : heap.MapRead(heap.Inputs.Cast(value.Type, type), value.Var, context);

    /// <summary>
    /// A <c>decimal</c> operator as the IOperation lowering applies it (ticket M4-002): a binary operator, <c>++</c> and
    /// <c>--</c> (an addition or subtraction of <c>1m</c>), <c>-</c> and a numeric conversion, each its
    /// <see cref="PureCatalogue"/> function. The catalogue has every operator <c>System.Decimal</c> declares but <c>+</c>,
    /// which Roslyn never calls (a test pins both).
    /// </summary>
    private Val Decimal(IMethodSymbol target, ImmutableArray<IrVar> arguments)
    {
        ITypeSymbol decimalType = compilation.GetSpecialType(SpecialType.System_Decimal);
        ImmutableArray<IrVar> operands = target.Parameters.Length == 1 && DecimalOperators.ContainsKey(target.Name)
            ? [arguments[0], Const(TypeMapper.Constant(decimalType, 1))]
            : arguments;
        PureCatalogue.Entry entry = target.Name switch
        {
            WellKnownMemberNames.UnaryNegationOperatorName => PureCatalogue.Negation(decimalType)!,
            _ when DecimalOperators.TryGetValue(target.Name, out BinaryOperatorKind kind) => PureCatalogue.Binary(kind, decimalType, decimalType)!,
            _ => PureCatalogue.Conversion(target.Parameters[0].Type, target.ReturnType)!,
        };
        return new(Apply(entry, operands, target.ReturnType), target.ReturnType);
    }

    /// <summary>A catalogued function (ticket M4-002), with the exceptions it raises in this context, runtime-sensitive as this side's runtime makes it.</summary>
    private IrVar Apply(PureCatalogue.Entry entry, ImmutableArray<IrVar> args, ITypeSymbol result, bool isChecked = false) =>
        Pure(entry.Function, entry.Raises(isChecked), entry.RuntimeSensitive(runtime), args, Map(result));

    /// <summary>
    /// An <see cref="IrPure"/> of <paramref name="function"/>, then, per exception it raises, a branch on its flag to where
    /// that exception goes, as <see cref="IrLowerer"/> applies one.
    /// </summary>
    private IrVar Pure(string function, ImmutableArray<string> throws, bool runtimeSensitive, ImmutableArray<IrVar> args, IrType result)
    {
        IrVar target = ssa.Temp(result);
        ImmutableArray<IrPureThrow> flags = [.. throws.Select(t => new IrPureThrow(ssa.Temp(Bool), t))];
        ssa.Emit(context.Current, new IrPure(target, flags, function, args) { RuntimeSensitive = runtimeSensitive });
        foreach (IrPureThrow flag in flags)
        {
            ThrowIf(flag.Flag, flag.ExceptionType);
        }

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

    /// <summary>Whether a fragment can read <paramref name="variable"/>: the IR has a type for it, and a value, which a caught exception has not.</summary>
    private bool IsReadable(ILVariable variable) => VariableType(variable) is not null && !caught.Contains(variable);

    /// <summary>A fragment's read of <paramref name="variable"/>: its value, and its null shadow after it when it has one.</summary>
    private IEnumerable<IrVar> Read(ILVariable variable)
    {
        Val value = Load(variable);
        return !IsThis(variable) && Shadow(Variable(variable)) is { } shadow ? [value.Var, ssa.Load(context.Current, shadow)] : [value.Var];
    }

    /// <summary>The C# type of IL's stack slot <paramref name="type"/>: <c>long</c>, <c>float</c> or <c>double</c> for theirs, else <c>int</c>.</summary>
    private ITypeSymbol Stack(StackType type) => compilation.GetSpecialType(type switch
    {
        StackType.I8 => SpecialType.System_Int64,
        StackType.F4 => SpecialType.System_Single,
        StackType.F8 => SpecialType.System_Double,
        _ => SpecialType.System_Int32,
    });

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

    private IrVar Not(IrVar value)
    {
        IrVar target = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrUnary(target, IrUnaryOp.BoolNot, value));
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

    /// <summary>
    /// Ends the current block, taken when <paramref name="condition"/> holds, at where an exception of
    /// <paramref name="exceptionType"/> goes from the statement being lowered; <see cref="PureCatalogue.AnyException"/>'s
    /// type is not known, as an opaque call's is not.
    /// </summary>
    private void ThrowIf(IrVar condition, string exceptionType)
    {
        IrBlockId thrown = Raise(
            exceptionType,
            string.Equals(exceptionType, PureCatalogue.AnyException, StringComparison.Ordinal) ? null : compilation.GetTypeByMetadataName(exceptionType));
        IrBlockId next = ssa.NewBlock();
        ssa.Terminate(context.Current, new IrBranch(condition, thrown, next));
        context.Current = next;
    }

    private void ThrowIfOverflows(IrOverflowOp op, IrVar left, IrVar right)
    {
        IrVar overflows = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrOverflows(overflows, op, left, right));
        ThrowIf(overflows, PureCatalogue.Overflow);
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
