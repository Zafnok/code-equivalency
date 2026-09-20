using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Lowers a C# method body to an SSA <see cref="IrProcedure"/> from Roslyn's control-flow graph
/// (ADR 0003; VERIFICATION-MODEL.md sections 2 and 3; ticket M2-003). Pass 1 maps each reachable
/// CFG block to draft IR blocks, making overflow, division-by-zero and call-threw edges explicit;
/// pass 2 is <see cref="SsaBuilder"/>. Anything not lowered becomes <see cref="IrOpaque"/> with a
/// reason naming the construct; this class never throws on unsupported input.
/// </summary>
internal sealed class IrLowerer
{
    private const string OverflowException = "System.OverflowException";

    private static readonly IrBool Bool = new();

    private readonly SsaBuilder ssa = new();
    private readonly RenameMap renames;
    private readonly IrType? returnType;
    private readonly Dictionary<ISymbol, SsaBuilder.Variable> variables = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, SsaBuilder.Variable> captures = [];
    private readonly Dictionary<CaptureId, SsaBuilder.Variable> captureTargets = [];
    private readonly Dictionary<string, IrBlockId> throwBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Region, IrBlockId Continuation), IrBlockId> copies = [];
    private Dictionary<int, IrBlockId> blockIds = [];
    private readonly Dictionary<SsaBuilder.Variable, SsaBuilder.Variable> shadows = [];
    private readonly Dictionary<string, SsaBuilder.Variable> slices = new(StringComparer.Ordinal);
    private readonly HeapInputs heap = new();
    private SwitchChains chains = null!;
    private CSharpCompilation compilation = null!;
    private ControlFlowGraph cfg = null!;
    private BasicBlock source = null!;
    private SourceSpan bodySpan = null!;
    private IrBlockId? handlerExit;
    private IrBlockId current = new(0);

    private IrLowerer(RenameMap renames, IrType? returnType)
    {
        this.renames = renames;
        this.returnType = returnType;
    }

    /// <summary>
    /// Lowers <paramref name="method"/>'s first declaration. A body that is not an <see cref="IMethodBodyOperation"/>
    /// (a constructor, an arrow-bodied property, an auto-accessor) is one whole-body <see cref="IrOpaque"/>.
    /// </summary>
    public static IrProcedure Lower(IMethodSymbol method, Compilation compilation, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        SyntaxNode syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
        SemanticModel model = compilation.GetSemanticModel(syntax.SyntaxTree);
        IOperation? operation = model.GetOperation(syntax);
        return operation is IMethodBodyOperation body
            ? Lower(body, model, renames)
            : Opaque(method, renames, operation?.Kind.ToString() ?? "no-body", Span(syntax));
    }

    public static IrProcedure Lower(IMethodBodyOperation body, SemanticModel model, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        IMethodSymbol method = (IMethodSymbol)model.GetDeclaredSymbol(body.Syntax)!;
        ControlFlowGraph graph = ControlFlowGraph.Create(body);
        SourceSpan span = Span(body.Syntax);
        // The CFG turns a loop into plain branches with a back edge, which the SSA builder handles; only
        // `foreach` is left, because the CFG desugars every one of them -- arrays included -- into the
        // enumerator pattern, whose `Current` property no map models (post-MVP ticket P1-004). `using`
        // and `lock` are out of this ticket's scope even though the CFG gives them ordinary regions.
        string? wholeBody = body.Descendants().Any(static o => o is IForEachLoopOperation) ? "foreach-enumerator"
            : body.Descendants().Any(static o => o is IUsingOperation or IUsingDeclarationOperation) ? "using"
            : body.Descendants().Any(static o => o is ILockOperation) ? "lock"
            : ExceptionRegions.HasUnsupportedCatch(graph.Root) ? "catch-filter"
            : null;
        if (wholeBody is not null)
        {
            return Opaque(method, renames, wholeBody, span);
        }

        (ImmutableArray<IrParameter> parameters, IrType? returnType) = Signature(method);
        IrLowerer lowerer = new(renames, returnType) { compilation = (CSharpCompilation)model.Compilation, cfg = graph };
        ImmutableArray<IrBlock> blocks = lowerer.LowerBlocks(graph, method, parameters, span);
        IrProcedure procedure = new(
            RoslynIdentity.Of(method, renames),
            [.. parameters, .. lowerer.heap.Parameters],
            returnType,
            blocks,
            new IrBlockId(0));
        Debug.Assert(IrValidator.Validate(procedure).IsEmpty, "lowered IR must validate");
        return procedure;
    }

    private static (ImmutableArray<IrParameter> Parameters, IrType? ReturnType) Signature(IMethodSymbol method) => (
        [.. method.Parameters.Select(static p => new IrParameter(
            new IrVar(p.Name, TypeMapper.Map(p.Type), p.Name),
            p.RefKind switch
            {
                RefKind.Ref => IrParameterKind.Ref,
                RefKind.Out => IrParameterKind.Out,
                _ => IrParameterKind.In,
            }))],
        method.ReturnsVoid ? null : TypeMapper.Map(method.ReturnType));

    /// <summary>One block: an opaque value (reason <paramref name="reason"/>) returned, by-ref parameters unchanged.</summary>
    private static IrProcedure Opaque(IMethodSymbol method, RenameMap renames, string reason, SourceSpan span)
    {
        (ImmutableArray<IrParameter> parameters, IrType? returnType) = Signature(method);
        IrVar? value = returnType is null ? null : new IrVar("$0", returnType);
        IrBlock block = new(
            new IrBlockId(0),
            [new IrOpaque(value, reason, span)],
            new IrReturn(value, [.. parameters.Where(static p => p.Kind != IrParameterKind.In).Select(static p => new IrOut(p.Var, p.Var))]));
        return new IrProcedure(RoslynIdentity.Of(method, renames), parameters, returnType, [block], block.Id);
    }

    private static SourceSpan Span(SyntaxNode syntax) => CSharpFrontend.ToSourceSpan(syntax.GetLocation());

    private ImmutableArray<IrBlock> LowerBlocks(ControlFlowGraph cfg, IMethodSymbol method, ImmutableArray<IrParameter> parameters, SourceSpan span)
    {
        bodySpan = span;
        chains = SwitchChains.Find(cfg);
        // A `finally` is never lowered in place: it is copied onto each path that leaves its `try`.
        ImmutableArray<BasicBlock> reachable =
            [.. cfg.Blocks.Where(b => b.IsReachable && !chains.IsAbsorbed(b.Ordinal) && ExceptionRegions.EnclosingFinally(b) is null)];
        foreach (BasicBlock block in reachable)
        {
            blockIds[block.Ordinal] = ssa.NewBlock();
        }

        ImmutableArray<(SsaBuilder.Variable, IrVar)>.Builder outs = ImmutableArray.CreateBuilder<(SsaBuilder.Variable, IrVar)>();
        for (int i = 0; i < parameters.Length; i++)
        {
            SsaBuilder.Variable variable = Declare(method.Parameters[i], parameters[i].Var, method.Parameters[i].Type);
            ssa.Store(current, variable, parameters[i].Var);
            if (Shadow(variable) is { } shadow)
            {
                ssa.Store(current, shadow, MapRead(heap.Nulls((IrSort)parameters[i].Var.Type), parameters[i].Var));
            }

            if (parameters[i].Kind != IrParameterKind.In)
            {
                outs.Add((variable, parameters[i].Var));
            }
        }

        foreach (BasicBlock block in reachable)
        {
            Fill(block, span);
        }

        return ssa.Build(new IrBlockId(0), outs.ToImmutable(), span);
    }

    private void Fill(BasicBlock block, SourceSpan span)
    {
        source = block;
        current = blockIds[block.Ordinal];
        foreach (IOperation operation in block.Operations)
        {
            Statement(operation);
        }

        Terminate(block, span);
    }

    /// <summary>
    /// A copy of a <c>finally</c> region that runs and then continues at <paramref name="continuation"/>
    /// (acceptance criterion 4: the blocks are duplicated onto every exit path). One copy per
    /// continuation; the copy's blocks live in their own block map, and its structured-exception-handling
    /// exit becomes the jump to <paramref name="continuation"/>.
    /// </summary>
    private IrBlockId Copy(ControlFlowRegion region, IrBlockId continuation)
    {
        if (copies.TryGetValue((region.FirstBlockOrdinal, continuation), out IrBlockId? existing))
        {
            return existing;
        }

        ImmutableArray<BasicBlock> blocks =
            [.. cfg.Blocks
                .Where(b => b.Ordinal >= region.FirstBlockOrdinal && b.Ordinal <= region.LastBlockOrdinal)
                .Where(b => b.IsReachable && !chains.IsAbsorbed(b.Ordinal) && ExceptionRegions.EnclosingFinally(b) == region)];
        Dictionary<int, IrBlockId> map = blocks.ToDictionary(static b => b.Ordinal, _ => ssa.NewBlock());
        IrBlockId entry = map[region.FirstBlockOrdinal];
        copies[(region.FirstBlockOrdinal, continuation)] = entry;

        (Dictionary<int, IrBlockId> outerBlocks, IrBlockId? outerExit, BasicBlock outerSource, IrBlockId outerCurrent) =
            (blockIds, handlerExit, source, current);
        (blockIds, handlerExit) = (map, continuation);
        foreach (BasicBlock block in blocks)
        {
            Fill(block, bodySpan);
        }

        (blockIds, handlerExit, source, current) = (outerBlocks, outerExit, outerSource, outerCurrent);
        return entry;
    }

    /// <summary>Runs <paramref name="finallys"/> in order and then continues at <paramref name="destination"/>.</summary>
    private IrBlockId Unwind(ImmutableArray<ControlFlowRegion> finallys, IrBlockId destination)
    {
        for (int i = finallys.Length - 1; i >= 0; i--)
        {
            destination = Copy(finallys[i], destination);
        }

        return destination;
    }

    /// <summary>The block a CFG branch jumps to, with every <c>finally</c> it leaves copied in front of it.</summary>
    private IrBlockId Destination(ControlFlowBranch branch) =>
        Unwind(branch.FinallyRegions, blockIds[branch.Destination!.Ordinal]);

    /// <summary>
    /// Where an exception of <paramref name="type"/> raised in the block being lowered goes: a matching
    /// <c>catch</c>, or the shared throw block for <paramref name="exceptionType"/>, behind the
    /// <c>finally</c> regions it leaves on the way.
    /// </summary>
    private IrBlockId Raise(string exceptionType, ITypeSymbol? type)
    {
        (ImmutableArray<ControlFlowRegion> finallys, ControlFlowRegion? handler, bool ambiguous) =
            ExceptionRegions.Route(compilation, source, type);
        if (ambiguous)
        {
            IrBlockId unknown = ssa.NewBlock();
            ssa.Emit(unknown, new IrOpaque(null, "call-throw-in-try", bodySpan));
            ssa.Terminate(unknown, new IrThrow(exceptionType, []));
            return unknown;
        }

        return Unwind(finallys, handler is null ? ThrowBlock(exceptionType) : blockIds[handler.FirstBlockOrdinal]);
    }

    private IrBlockId ThrowBlock(string exceptionType)
    {
        if (!throwBlocks.TryGetValue(exceptionType, out IrBlockId? thrown))
        {
            thrown = ssa.NewBlock();
            ssa.Terminate(thrown, new IrThrow(exceptionType, []));
            throwBlocks[exceptionType] = thrown;
        }

        return thrown;
    }

    private void Terminate(BasicBlock block, SourceSpan span)
    {
        if (block.Kind == BasicBlockKind.Exit)
        {
            // Reached by a Regular fall-through: a void method's end, or erroneous code in a non-void one.
            if (returnType is null)
            {
                ssa.Terminate(current, new IrReturn(null, []));
            }
            else
            {
                OpaqueExit("missing-return", span);
            }

            return;
        }

        if (chains.Head(block.Ordinal) is { } chain)
        {
            Switch(chain);
            return;
        }

        ControlFlowBranch fallThrough = block.FallThroughSuccessor!;
        if (block.ConditionalSuccessor is { } conditional)
        {
            IrVar condition = Value(block.BranchValue!);
            IrBlockId jump = Destination(conditional);
            IrBlockId next = Destination(fallThrough);
            ssa.Terminate(current, block.ConditionKind == ControlFlowConditionKind.WhenTrue
                ? new IrBranch(condition, jump, next)
                : new IrBranch(condition, next, jump));
            return;
        }

        switch (fallThrough.Semantics)
        {
            case ControlFlowBranchSemantics.Regular:
                ssa.Terminate(current, new IrGoto(Destination(fallThrough)));
                break;
            case ControlFlowBranchSemantics.Return:
                Return(Value(block.BranchValue!), fallThrough); // Value may move `current` past exception edges
                break;
            case ControlFlowBranchSemantics.Throw when block.BranchValue is { } thrown:
                Throw(thrown, span);
                break;
            case ControlFlowBranchSemantics.StructuredExceptionHandling when handlerExit is { } exit:
                ssa.Terminate(current, new IrGoto(exit));
                break;
            default:
                // `throw;`: the exception in flight is not modelled. Also the edge erroneous code produces.
                OpaqueExit("rethrow", span);
                break;
        }
    }

    /// <summary>A return runs every enclosing <c>finally</c> after evaluating its value and before exiting.</summary>
    private void Return(IrVar value, ControlFlowBranch branch)
    {
        if (branch.FinallyRegions.IsEmpty)
        {
            ssa.Terminate(current, new IrReturn(value, []));
            return;
        }

        IrBlockId exit = ssa.NewBlock();
        ssa.Terminate(exit, new IrReturn(value, []));
        ssa.Terminate(current, new IrGoto(Unwind(branch.FinallyRegions, exit)));
    }

    /// <summary>
    /// <c>throw new T(...)</c> (ticket M2-004 acceptance criterion 3): the constructor call first, then
    /// <c>IrThrow("T")</c> on T's static type. Throwing anything else leaves the type unknown, so it is opaque.
    /// </summary>
    private void Throw(IOperation thrown, SourceSpan span)
    {
        IOperation value = thrown;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (value is not IObjectCreationOperation creation)
        {
            OpaqueExit("Throw", span);
            return;
        }

        Lower(creation); // may move `current` past the constructor's own threw branch
        ssa.Terminate(current, new IrGoto(Raise(TypeMapper.MetadataName(creation.Type!), creation.Type)));
    }

    /// <summary>A chain of equality tests on one scrutinee, folded back into one terminator (acceptance criterion 2).</summary>
    private void Switch(SwitchChains.Chain chain)
    {
        IrVar scrutinee = Value(chain.Scrutinee);
        ssa.Terminate(current, new IrSwitch(
            scrutinee,
            [.. chain.Cases.Select(c => (TypeMapper.Constant(c.ConstantType, c.Constant), blockIds[c.Target.Ordinal]))],
            blockIds[chain.Default.Ordinal]));
    }

    /// <summary>
    /// A pattern test (acceptance criterion 2): a constant pattern is an equality, a discard is <c>true</c>,
    /// and every other pattern is opaque, which is how a pattern switch beyond constant cases stops here.
    /// </summary>
    private IrVar? Match(IIsPatternOperation pattern)
    {
        switch (pattern.Pattern)
        {
            case IDiscardPatternOperation:
                return Const(new IrBoolValue(true));
            case IConstantPatternOperation { Value: { Type: { } type, ConstantValue: { HasValue: true, Value: { } constant } } }
                when TypeMapper.Map(type) is IrBitVec or IrBool && TypeMapper.Map(pattern.Value.Type!) == TypeMapper.Map(type):
                return Emit(IrBinaryOp.Eq, Value(pattern.Value), Constant(type, constant), Bool);
            default:
                return Opaque(pattern, "switch-pattern");
        }
    }

    private void OpaqueExit(string reason, SourceSpan span)
    {
        IrVar? value = returnType is null ? null : ssa.Temp(returnType);
        ssa.Emit(current, new IrOpaque(value, reason, span));
        ssa.Terminate(current, new IrReturn(value, []));
    }

    private void Statement(IOperation operation)
    {
        if (operation is IFlowCaptureOperation capture)
        {
            // A captured local or parameter may later be an assignment target (the CFG captures the
            // left-hand side first when the right-hand side branches), so remember it as an lvalue too.
            if (Target(capture.Value) is { } target)
            {
                captureTargets[capture.Id] = target;
            }

            IrVar value = Value(capture.Value);
            SsaBuilder.Variable captured = Capture(capture.Id, capture.Value.Type!);
            ssa.Store(current, captured, value);
            StoreShadow(captured, capture.Value, value);
            return;
        }

        Lower(operation);
    }

    private IrVar Value(IOperation operation) => Lower(operation)!;

    /// <summary>The operation's value, or null for an operation without one (a statement, a void call).</summary>
    private IrVar? Lower(IOperation operation)
    {
        if (operation is { ConstantValue.HasValue: true, Type: { } constantType })
        {
            return Constant(constantType, operation.ConstantValue.Value);
        }

        switch (operation)
        {
            case IExpressionStatementOperation statement:
                Lower(statement.Operation);
                return null;
            case ILocalReferenceOperation local:
                return ssa.Load(current, Local(local.Local));
            case IParameterReferenceOperation parameter when variables.TryGetValue(parameter.Parameter, out SsaBuilder.Variable? variable):
                return ssa.Load(current, variable);
            case IFlowCaptureReferenceOperation reference:
                return ssa.Load(current, Capture(reference.Id, reference.Type!));
            case ISimpleAssignmentOperation { IsRef: false } assignment:
                return Assign(assignment);
            case IFieldReferenceOperation field:
                return ReadSlice(Field(field));
            case IArrayElementReferenceOperation element:
                return Element(element) is { } read ? ReadSlice(read) : Opaque(element, element.Kind.ToString());
            case IPropertyReferenceOperation property when ArrayLength(property) is { } length:
                return length;
            case IConversionOperation conversion:
                return Convert(conversion);
            case IBinaryOperation binary:
                return Binary(binary);
            case IUnaryOperation unary:
                return Unary(unary);
            case ICompoundAssignmentOperation compound:
                return Compound(compound);
            case IIncrementOrDecrementOperation step:
                return Step(step);
            case IInvocationOperation invocation:
                return Invoke(invocation);
            case IObjectCreationOperation creation:
                return Create(creation);
            case IIsPatternOperation pattern:
                return Match(pattern);
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: INamedTypeSymbol { IsValueType: false } type }:
                return heap.This(type);
            default:
                return Opaque(operation, operation.Kind.ToString());
        }
    }

    private SsaBuilder.Variable Local(ILocalSymbol local) =>
        variables.TryGetValue(local, out SsaBuilder.Variable? variable)
            ? variable
            : Declare(local, new IrVar(local.Name, TypeMapper.Map(local.Type), local.Name), local.Type);

    private SsaBuilder.Variable Capture(CaptureId id, ITypeSymbol type)
    {
        if (!captures.TryGetValue(id, out SsaBuilder.Variable? variable))
        {
            variable = Shadowed(new IrVar($"$c{captures.Count.ToString(CultureInfo.InvariantCulture)}", TypeMapper.Map(type)), type);
            captures[id] = variable;
        }

        return variable;
    }

    private SsaBuilder.Variable Declare(ISymbol symbol, IrVar template, ITypeSymbol type)
    {
        SsaBuilder.Variable variable = Shadowed(template, type);
        variables[symbol] = variable;
        return variable;
    }

    /// <summary>A reference-typed variable is paired with a Bool <c>&lt;name&gt;.isNull</c> shadow (acceptance criterion 5).</summary>
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

    /// <summary>The shadow of the variable an lvalue names, or null when it names none or is not reference-typed.</summary>
    private SsaBuilder.Variable? ShadowOf(IOperation lvalue) => Target(lvalue) is { } variable ? Shadow(variable) : null;

    /// <summary>
    /// Whether <paramref name="source"/> is null, or null when it provably is not. A <c>new</c> is never
    /// null and neither is <c>this</c>; a variable carries its own shadow; anything else asks the
    /// <c>null.&lt;Sort&gt;</c> map, so equal references are equally null.
    /// </summary>
    private IrVar? Nullness(IOperation source, IrVar value)
    {
        IOperation unwrapped = Unwrap(source);
        if (unwrapped is IObjectCreationOperation or IInstanceReferenceOperation)
        {
            return null;
        }

        if (ShadowOf(unwrapped) is { } shadow)
        {
            return ssa.Load(current, shadow);
        }

        return unwrapped.ConstantValue is { HasValue: true, Value: null }
            ? Const(new IrBoolValue(true))
            : MapRead(heap.Nulls((IrSort)value.Type), value);
    }

    /// <summary>Records the nullness of a value stored into a reference-typed variable.</summary>
    private void StoreShadow(SsaBuilder.Variable target, IOperation source, IrVar value)
    {
        if (Shadow(target) is { } shadow)
        {
            ssa.Store(current, shadow, Nullness(source, value) ?? Const(new IrBoolValue(false)));
        }
    }

    /// <summary>A dereference of a value that is not provably non-null throws <c>NullReferenceException</c> when it is.</summary>
    private void ThrowIfNull(IOperation source, IrVar value)
    {
        if (Nullness(source, value) is { } isNull)
        {
            ThrowIf(isNull, "System.NullReferenceException");
        }
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    /// <summary>
    /// The heap slice an assignment target names (acceptance criterion 6), or null when it is not a
    /// field or a single-dimensional array element of a variable.
    /// </summary>
    private Access? Slice(IOperation lvalue) => lvalue switch
    {
        IFieldReferenceOperation field => Field(field),
        IArrayElementReferenceOperation element => Element(element),
        _ => null,
    };

    /// <summary>A field is a map from its receiver, or from its declaring type's token when it is static.</summary>
    private Access Field(IFieldReferenceOperation field)
    {
        IrVar key;
        if (field.Instance is { } instance)
        {
            key = Value(instance);
            if (!instance.Type!.IsValueType)
            {
                ThrowIfNull(instance, key);
            }
        }
        else
        {
            key = Const(HeapInputs.Token(field.Field));
        }

        return new Access(Versioned(heap.Field(field.Field)), key, null);
    }

    /// <summary>
    /// An array element is a map from a bv32 index, bounded by the array variable's own length var. The
    /// unsigned comparison catches a negative index too. Null when the array is not a plain variable,
    /// the index is not bv32, or the array has several dimensions; nothing is emitted in that case.
    /// </summary>
    private Access? Element(IArrayElementReferenceOperation element)
    {
        if (element.Indices is not [{ Type: { } indexType }]
            || TypeMapper.Map(indexType) is not IrBitVec { Width: 32 }
            || Target(element.ArrayReference) is not { } array)
        {
            return null;
        }

        IrVar reference = Value(element.ArrayReference);
        ThrowIfNull(element.ArrayReference, reference);
        IrVar index = Value(element.Indices[0]);
        return new Access(
            Versioned(heap.Elements(array.Template.Name, TypeMapper.Map(element.Type!))),
            index,
            heap.Length(array.Template.Name));
    }

    /// <summary><c>a.Length</c> on an array variable is that variable's length var; every other property stays opaque.</summary>
    private IrVar? ArrayLength(IPropertyReferenceOperation property)
    {
        if (property is not { Property: { Name: "Length", ContainingType.SpecialType: SpecialType.System_Array }, Instance: { } instance }
            || Target(instance) is not { } array)
        {
            return null;
        }

        ThrowIfNull(instance, Value(instance));
        return heap.Length(array.Template.Name);
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
    private void Bounds(Access access)
    {
        if (access.Length is { } length)
        {
            ThrowIf(Emit(IrBinaryOp.Uge, access.Key, length, Bool), "System.IndexOutOfRangeException");
        }
    }

    private IrVar ReadSlice(Access access)
    {
        Bounds(access);
        return MapRead(ssa.Load(current, access.Map), access.Key);
    }

    private void WriteSlice(Access access, IrVar value)
    {
        Bounds(access);
        IrVar map = ssa.Load(current, access.Map);
        IrVar updated = ssa.Temp(map.Type);
        ssa.Emit(current, new IrMapWrite(updated, map, access.Key, value));
        ssa.Store(current, access.Map, updated);
    }

    private IrVar MapRead(IrVar map, IrVar key)
    {
        IrVar target = ssa.Temp(((IrMap)map.Type).Value);
        ssa.Emit(current, new IrMapRead(target, map, key));
        return target;
    }

    private IrVar? Opaque(IOperation operation, string reason)
    {
        IrVar? target = operation.Type is { SpecialType: not SpecialType.System_Void } type ? ssa.Temp(TypeMapper.Map(type)) : null;
        ssa.Emit(current, new IrOpaque(target, reason, Span(operation.Syntax)));
        return target;
    }

    private IrVar Constant(ITypeSymbol type, object? value) => Const(TypeMapper.Constant(type, value));

    private IrVar Const(IrValue value)
    {
        IrVar target = ssa.Temp(value.Type);
        ssa.Emit(current, new IrConst(target, value));
        return target;
    }

    private IrVar Zero(IrType type) => Const(new IrBitVecValue(((IrBitVec)type).Width, 0));

    private IrVar Emit(IrBinaryOp op, IrVar left, IrVar right, IrType type)
    {
        IrVar target = ssa.Temp(type);
        ssa.Emit(current, new IrBinary(target, op, left, right));
        return target;
    }

    /// <summary>
    /// Ends the current block with a branch, taken when <paramref name="condition"/> holds, to wherever an
    /// exception of <paramref name="exceptionType"/> goes from here: a matching <c>catch</c> or the shared
    /// throw block, behind any <c>finally</c> it leaves. A null <paramref name="type"/> means the type is
    /// not known, which is the case for an opaque call's <c>threw</c> flag.
    /// </summary>
    private void ThrowIf(IrVar condition, string exceptionType, bool known = true)
    {
        IrBlockId thrown = Raise(exceptionType, known ? compilation.GetTypeByMetadataName(exceptionType) : null);
        IrBlockId next = ssa.NewBlock();
        ssa.Terminate(current, new IrBranch(condition, thrown, next));
        current = next;
    }

    private void ThrowIfOverflows(IrOverflowOp op, IrVar left, IrVar right)
    {
        IrVar overflows = ssa.Temp(Bool);
        ssa.Emit(current, new IrOverflows(overflows, op, left, right));
        ThrowIf(overflows, OverflowException);
    }

    private IrVar? Assign(ISimpleAssignmentOperation assignment)
    {
        if (Slice(assignment.Target) is { } slice)
        {
            // C# evaluates the target's receiver and index, then the value, and only then stores,
            // so the bounds check comes after the value in an assignment but before a read.
            IrVar written = Value(assignment.Value);
            WriteSlice(slice, written);
            return written;
        }

        if (Target(assignment.Target) is not { } target)
        {
            return Opaque(assignment, assignment.Target.Kind.ToString());
        }

        IrVar value = Value(assignment.Value);
        ssa.Store(current, target, value);
        StoreShadow(target, assignment.Value, value);
        return value;
    }

    /// <summary>The variable an lvalue names, or null when it is not a local or parameter of this method.</summary>
    private SsaBuilder.Variable? Target(IOperation lvalue) => lvalue switch
    {
        ILocalReferenceOperation local => Local(local.Local),
        IParameterReferenceOperation parameter => variables.GetValueOrDefault(parameter.Parameter),
        IFlowCaptureReferenceOperation reference => captureTargets.GetValueOrDefault(reference.Id),
        _ => null,
    };

    /// <summary>Integral to integral only: extension follows the source's signedness; a checked narrowing throws when the value does not fit.</summary>
    private IrVar? Convert(IConversionOperation conversion)
    {
        if (conversion.OperatorMethod is not null
            || conversion.Operand.Type is not { } from
            || TypeMapper.Map(from) is not IrBitVec source
            || TypeMapper.Map(conversion.Type!) is not IrBitVec target)
        {
            return Opaque(conversion, conversion.Kind.ToString());
        }

        bool fromSigned = TypeMapper.IsSigned(from);
        bool toSigned = TypeMapper.IsSigned(conversion.Type!);
        IrVar value = Value(conversion.Operand);
        IrVar result = Resize(value, target, fromSigned);
        if (conversion.IsChecked && !conversion.Conversion.IsImplicit)
        {
            ThrowIfItDoesNotFit(value, result, fromSigned, toSigned);
        }

        return result;
    }

    /// <summary>Throws when <paramref name="result"/> does not round-trip to <paramref name="value"/>, or when the signed side of a signedness change is negative.</summary>
    private void ThrowIfItDoesNotFit(IrVar value, IrVar result, bool fromSigned, bool toSigned)
    {
        IrVar lost = Emit(IrBinaryOp.Ne, Resize(result, (IrBitVec)value.Type, toSigned), value, Bool);
        if (fromSigned != toSigned)
        {
            IrVar signedSide = fromSigned ? value : result;
            lost = Emit(IrBinaryOp.Or, lost, Emit(IrBinaryOp.Slt, signedSide, Zero(signedSide.Type), Bool), Bool);
        }

        ThrowIf(lost, OverflowException);
    }

    private IrVar Resize(IrVar value, IrBitVec type, bool signed)
    {
        int width = ((IrBitVec)value.Type).Width;
        if (width == type.Width)
        {
            return value;
        }

        IrVar target = ssa.Temp(type);
        ssa.Emit(current, new IrUnary(target, width < type.Width ? (signed ? IrUnaryOp.SExt : IrUnaryOp.ZExt) : IrUnaryOp.Trunc, value));
        return target;
    }

    private IrVar? Binary(IBinaryOperation binary)
    {
        if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals && NullTest(binary) is { } test)
        {
            return test;
        }

        if (binary is { OperatorKind: BinaryOperatorKind.Add, LeftOperand.Type.SpecialType: SpecialType.System_String, RightOperand.Type.SpecialType: SpecialType.System_String })
        {
            // Strings stay uninterpreted, so `a + b` is the call the compiler makes.
            return Call(
                new CallIdentity(ProcedureIdentityNormalizer.Member("System", "String", "Concat", 0, ["string", "string"], renames).Value),
                [Value(binary.LeftOperand), Value(binary.RightOperand)],
                TypeMapper.Map(binary.Type!));
        }

        bool signed = TypeMapper.IsSigned(binary.LeftOperand.Type!);
        IrVar left = Value(binary.LeftOperand);
        IrVar right = Value(binary.RightOperand);
        if (OperatorMapper.Binary(binary.OperatorKind, signed, left.Type, right.Type) is not { } op)
        {
            return Opaque(binary, binary.Kind.ToString());
        }

        return OperatorMapper.IsShift(op)
            ? Shift(op, left, right, (IrBitVec)left.Type)
            : Arithmetic(op, left, right, signed, binary.IsChecked, TypeMapper.Map(binary.Type!));
    }

    /// <summary><c>x == null</c> and <c>x != null</c> compare the shadow (acceptance criterion 5); null when neither side is <c>null</c>.</summary>
    private IrVar? NullTest(IBinaryOperation binary)
    {
        IOperation? other = (IsNull(binary.LeftOperand), IsNull(binary.RightOperand)) switch
        {
            (false, true) => binary.LeftOperand,
            (true, false) => binary.RightOperand,
            _ => null,
        };
        if (other is not { Type.IsReferenceType: true })
        {
            return null;
        }

        IrVar isNull = Nullness(other, Value(other)) ?? Const(new IrBoolValue(false));
        return binary.OperatorKind == BinaryOperatorKind.Equals ? isNull : EmitUnary(IrUnaryOp.BoolNot, isNull);
    }

    private static bool IsNull(IOperation operand) => Unwrap(operand).ConstantValue is { HasValue: true, Value: null };

    /// <summary>C# masks the shift count to the left operand's width (ECMA-334 shift operators); the IR shift does not.</summary>
    private IrVar Shift(IrBinaryOp op, IrVar left, IrVar count, IrBitVec type)
    {
        IrVar mask = Const(new IrBitVecValue(((IrBitVec)count.Type).Width, (ulong)type.Width - 1));
        IrVar masked = Resize(Emit(IrBinaryOp.And, count, mask, count.Type), type, signed: false);
        return Emit(op, left, masked, type);
    }

    private IrVar Arithmetic(IrBinaryOp op, IrVar left, IrVar right, bool signed, bool isChecked, IrType type)
    {
        if (op is IrBinaryOp.SDiv or IrBinaryOp.SRem or IrBinaryOp.UDiv or IrBinaryOp.URem)
        {
            ThrowIf(Emit(IrBinaryOp.Eq, right, Zero(right.Type), Bool), "System.DivideByZeroException");
            if (signed)
            {
                // .NET throws on MinValue / -1 and MinValue % -1 in unchecked code too.
                ThrowIfOverflows(IrOverflowOp.SDiv, left, right);
            }
        }
        else if (isChecked && OperatorMapper.Overflow(op, signed) is { } overflow)
        {
            ThrowIfOverflows(overflow, left, right);
        }

        return Emit(op, left, right, type);
    }

    /// <summary>
    /// <c>x op= v</c> (ticket M2-004 acceptance criterion 9): read, promote to the operator's type, operate
    /// with the binary operator's exception edges, narrow back to the target type, write. A shift takes its
    /// operator type from the promoted target; every other operator from the right operand, which Roslyn has
    /// already converted to it.
    /// </summary>
    private IrVar? Compound(ICompoundAssignmentOperation compound)
    {
        ITypeSymbol right = compound.Value.Type!;
        (IrBitVec Type, bool Signed)? operands = OperatorMapper.IsShiftKind(compound.OperatorKind)
            ? TypeMapper.Promote(compound.Target.Type!)
            : TypeMapper.Map(right) is IrBitVec bits ? (bits, TypeMapper.IsSigned(right)) : null;
        return compound.OperatorMethod is null && operands is { } promoted
            ? Update(compound, compound.Target, compound.OperatorKind, Value(compound.Value), promoted, compound.IsChecked, isPostfix: false)
            : Opaque(compound, compound.Kind.ToString());
    }

    /// <summary><c>x++</c>, <c>--x</c>: the right operand is a promoted <c>1</c>; postfix yields the value read.</summary>
    private IrVar? Step(IIncrementOrDecrementOperation step)
    {
        if (TypeMapper.Promote(step.Type!) is not { } promoted)
        {
            return Opaque(step, step.Kind.ToString());
        }

        IrVar one = Const(new IrBitVecValue(promoted.Type.Width, 1));
        BinaryOperatorKind kind = step.Kind == OperationKind.Increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;
        return Update(step, step.Target, kind, one, promoted, step.IsChecked, step.IsPostfix);
    }

    private IrVar? Update(IOperation node, IOperation lvalue, BinaryOperatorKind kind, IrVar right, (IrBitVec Type, bool Signed) promoted, bool isChecked, bool isPostfix)
    {
        if (Target(lvalue) is not { } target || TypeMapper.Map(lvalue.Type!) is not IrBitVec narrow)
        {
            return Opaque(node, lvalue.Kind.ToString());
        }

        // Never null here: a shift takes operands of any two widths, and every other compound operator
        // was given the right operand's own bitvector type.
        IrBinaryOp op = OperatorMapper.Binary(kind, promoted.Signed, promoted.Type, right.Type)!.Value;
        bool targetSigned = TypeMapper.IsSigned(lvalue.Type!);
        IrVar old = ssa.Load(current, target);
        IrVar wide = Resize(old, promoted.Type, targetSigned);
        IrVar computed = OperatorMapper.IsShift(op)
            ? Shift(op, wide, right, promoted.Type)
            : Arithmetic(op, wide, right, promoted.Signed, isChecked, promoted.Type);
        IrVar result = Resize(computed, narrow, promoted.Signed);
        if (isChecked && narrow != promoted.Type)
        {
            ThrowIfItDoesNotFit(computed, result, promoted.Signed, targetSigned);
        }

        ssa.Store(current, target, result);
        return isPostfix ? old : result;
    }

    private IrVar? Unary(IUnaryOperation unary)
    {
        IrVar operand = Value(unary.Operand);
        switch (unary.OperatorKind, operand.Type)
        {
            case (UnaryOperatorKind.Not, IrBool):
                return EmitUnary(IrUnaryOp.BoolNot, operand);
            case (UnaryOperatorKind.BitwiseNegation, IrBitVec):
                return EmitUnary(IrUnaryOp.Not, operand);
            case (UnaryOperatorKind.Minus, IrBitVec):
                if (unary.IsChecked)
                {
                    ThrowIfOverflows(IrOverflowOp.SSub, Zero(operand.Type), operand);
                }

                return EmitUnary(IrUnaryOp.Neg, operand);
            case (UnaryOperatorKind.Plus, IrBitVec):
                return operand;
            default:
                return Opaque(unary, unary.Kind.ToString());
        }
    }

    private IrVar EmitUnary(IrUnaryOp op, IrVar operand)
    {
        IrVar target = ssa.Temp(operand.Type);
        ssa.Emit(current, new IrUnary(target, op, operand));
        return target;
    }

    /// <summary><c>new T(...)</c>: an opaque call to the constructor yielding the new object.</summary>
    private IrVar? Create(IObjectCreationOperation creation) =>
        creation.Arguments.Any(static a => a.Parameter!.RefKind is RefKind.Ref or RefKind.Out)
            ? Opaque(creation, "ref-argument")
            : Call(CallIdentityFactory.Of(creation.Constructor!, renames), [.. Arguments([], creation.Arguments)], TypeMapper.Map(creation.Type!));

    /// <summary>An opaque call (receiver first, then arguments in parameter order) that may throw System.Exception.</summary>
    private IrVar? Invoke(IInvocationOperation invocation)
    {
        if (invocation.Arguments.Any(static a => a.Parameter!.RefKind is RefKind.Ref or RefKind.Out))
        {
            return Opaque(invocation, "ref-argument");
        }

        List<IrVar> receiver = [];
        if (invocation.Instance is { } instance)
        {
            IrVar value = Value(instance);
            receiver.Add(value);
            if (!instance.Type!.IsValueType)
            {
                ThrowIfNull(instance, value);
            }
        }

        return Call(
            CallIdentityFactory.Of(invocation.TargetMethod, renames),
            [.. Arguments(receiver, invocation.Arguments)],
            invocation.TargetMethod.ReturnsVoid ? null : TypeMapper.Map(invocation.Type!));
    }

    /// <summary>The receiver, then the arguments in parameter order; each is evaluated in source order first.</summary>
    private IEnumerable<IrVar> Arguments(IEnumerable<IrVar> receiver, ImmutableArray<IArgumentOperation> arguments) =>
        receiver.Concat(arguments
            .Select(a => (a.Parameter!.Ordinal, Value: Value(a.Value)))
            .ToList()
            .OrderBy(static a => a.Ordinal)
            .Select(static a => a.Value));

    private IrVar? Call(CallIdentity callee, ImmutableArray<IrVar> args, IrType? returns)
    {
        IrVar? target = returns is null ? null : ssa.Temp(returns);
        IrVar threw = ssa.Temp(Bool);
        ssa.Emit(current, new IrCall(target, threw, callee, args));
        ThrowIf(threw, "System.Exception", known: false);
        return target;
    }

    /// <summary>
    /// One access to a heap slice: the SSA variable holding the map's current version, the key, and the
    /// bound the key must be under (an array variable's length var; null for a field).
    /// </summary>
    private readonly record struct Access(SsaBuilder.Variable Map, IrVar Key, IrVar? Length);
}
