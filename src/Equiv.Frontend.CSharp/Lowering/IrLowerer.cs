using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Lowers a C# method body to an SSA <see cref="IrProcedure"/> from Roslyn's control-flow graph
/// (ADR 0003; VERIFICATION-MODEL.md sections 2 and 3; ticket M2-003). Pass 1 maps each reachable
/// CFG block to draft IR blocks, making overflow, division-by-zero and call-threw edges explicit;
/// pass 2 is <see cref="SsaBuilder"/>. Anything not lowered becomes <see cref="IrOpaque"/> with a
/// reason naming the construct; this class never throws on unsupported input. The heap
/// (<see cref="HeapLowerer"/>) and the exception regions (<see cref="ExceptionLowerer"/>) are
/// collaborators reached through one instance each (ticket P1-003); everything either needs while
/// filling a block is a <see cref="LoweringContext"/> passed explicitly, never a swapped field. On the legacy side, the
/// API-equivalence entries it is given rewrite calls and map sort names to their modern counterparts (ADR 0020; ticket
/// M3-009); the modern side is given none.
/// </summary>
internal sealed class IrLowerer
{
    private const string OverflowException = "System.OverflowException";

    private static readonly IrBool Bool = new();

    private readonly SsaBuilder ssa = new();
    private readonly RenameMap renames;
    private readonly ImmutableArray<string> suppressedRuntimeChanges;
    private readonly Catalogue catalogue;
    private readonly IrType? returnType;
    private readonly Dictionary<ISymbol, SsaBuilder.Variable> variables = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, SsaBuilder.Variable> captures = [];
    private readonly Dictionary<CaptureId, SsaBuilder.Variable> captureTargets = [];
    private readonly Dictionary<CaptureId, PropertyAccess> propertyTargets = [];
    private HashSet<CaptureId> assignedCaptures = [];
    private readonly Dictionary<SsaBuilder.Variable, SsaBuilder.Variable> shadows = [];
    private HeapLowerer heap = null!;
    private ExceptionLowerer exceptions = null!;
    private SwitchChains chains = null!;
    private CSharpCompilation compilation = null!;
    private ControlFlowGraph cfg = null!;
    private SourceSpan bodySpan = null!;
    private INamedTypeSymbol receiver = null!;

    private IrLowerer(RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges, Catalogue catalogue, IrType? returnType)
    {
        this.renames = renames;
        this.suppressedRuntimeChanges = suppressedRuntimeChanges;
        this.catalogue = catalogue;
        this.returnType = returnType;
    }

    /// <summary>
    /// Lowers <paramref name="method"/>'s first declaration. An instance constructor is lowered like a method, its base or
    /// <c>this</c> initializer first, unless it leaves out the field initializers C# runs ahead of it (reason
    /// <c>field-initializer</c>, ticket M4-001). Any other body that is not an <see cref="IMethodBodyOperation"/> (a static or
    /// primary constructor, an arrow-bodied property, an auto-accessor) is one whole-body <see cref="IrOpaque"/>.
    /// A call to a member listed in <paramref name="suppressedRuntimeChanges"/> is not flagged runtime-changed. A method
    /// whose bound code is erroneous is one whole-body <see cref="IrOpaque"/> per cause, with reason
    /// <see cref="Unknown.UnboundOpaqueReason"/> (ADR 0029 decision 2), and is not lowered further.
    /// </summary>
    public static IrProcedure Lower(IMethodSymbol method, Compilation compilation, RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges) =>
        Lower(method, compilation, renames, suppressedRuntimeChanges, []).Body;

    /// <summary>
    /// As the four-argument overload, applying <paramref name="equivalences"/> (the legacy side's enabled catalogue
    /// entries, or none for the modern side), and returning the sorted ids of the entries that fired.
    /// </summary>
    public static (IrProcedure Body, ImmutableArray<string> EquivalencesApplied) Lower(
        IMethodSymbol method, Compilation compilation, RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges, ImmutableArray<ApiEquivalence> equivalences)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(compilation);
        Catalogue entries = new(equivalences);
        SyntaxNode syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
        SemanticModel model = compilation.GetSemanticModel(syntax.SyntaxTree);
        IOperation? operation = model.GetOperation(syntax);
        ImmutableArray<SourceSpan> unbound = UnboundCauses(syntax, model, operation);
        IrProcedure procedure = (unbound.IsEmpty, operation) switch
        {
            (false, _) => Opaque(method, renames, entries, Unknown.UnboundOpaqueReason, unbound),
            (true, IMethodBodyOperation body) => Lower(body, model, renames, suppressedRuntimeChanges, entries),
            (true, IConstructorBodyOperation body) when method.MethodKind == MethodKind.Constructor && syntax is ConstructorDeclarationSyntax declaration =>
                OmitsFieldInitializers(method, declaration)
                    ? Opaque(method, renames, entries, "field-initializer", [Span(syntax)])
                    : Lower(body, model, renames, suppressedRuntimeChanges, entries),
            _ => Opaque(method, renames, entries, operation?.Kind.ToString() ?? "no-body", [Span(syntax)]),
        };
        return (procedure, [.. entries.Applied]);
    }

    /// <summary>
    /// Lowers <paramref name="body"/> as it is bound. It does not check for erroneous code; the symbol overload does, and
    /// is the one the frontend uses. Erroneous constructs reaching here lower to named opaques (<c>Invalid</c>, <c>rethrow</c>).
    /// </summary>
    public static IrProcedure Lower(IMethodBodyBaseOperation body, SemanticModel model, RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(model);
        return Lower(body, model, renames, suppressedRuntimeChanges, new Catalogue([]));
    }

    private static IrProcedure Lower(IMethodBodyBaseOperation body, SemanticModel model, RenameMap renames, ImmutableArray<string> suppressedRuntimeChanges, Catalogue catalogue)
    {
        IMethodSymbol method = (IMethodSymbol)model.GetDeclaredSymbol(body.Syntax)!;
        // A constructor's graph starts with its initializer: a call to the base or `this` constructor on `this`.
        ControlFlowGraph graph = body is IConstructorBodyOperation constructor ? ControlFlowGraph.Create(constructor) : ControlFlowGraph.Create((IMethodBodyOperation)body);
        SourceSpan span = Span(body.Syntax);
        // `async` is checked first: an `await`'s state machine is not modelled (ticket M4-006), and
        // checking it ahead of the other whole-body cases keeps an async method from being classified
        // by whichever of those constructs its body happens to also contain.
        // The CFG turns a loop into plain branches with a back edge, which the SSA builder handles, and
        // desugars `foreach` and `using` into calls, conversions and a `finally` (ticket M4-001). `lock`
        // stays whole-body opaque: its desugaring passes `ref` to `Monitor.Enter` (ticket M4-003).
        string? wholeBody = body switch
        {
            _ when method.IsAsync => "async",
            _ when body.Descendants().Any(static o => o is ILockOperation) => "lock",
            _ when ExceptionRegions.HasUnsupportedCatch(graph.Root) => "catch-filter",
            _ => null,
        };
        if (wholeBody is not null)
        {
            return Opaque(method, renames, catalogue, wholeBody, [span]);
        }

        (ImmutableArray<IrParameter> parameters, IrType? returnType) = Signature(method, catalogue.Sorts);
        IrLowerer lowerer = new(renames, suppressedRuntimeChanges, catalogue, returnType) { compilation = (CSharpCompilation)model.Compilation, cfg = graph };
        ImmutableArray<IrBlock> blocks = lowerer.LowerBlocks(method, parameters, span);
        IrProcedure procedure = new(
            RoslynIdentity.Of(method, renames),
            [.. parameters, .. lowerer.heap.Inputs.Parameters],
            returnType,
            blocks,
            new IrBlockId(0));
        Debug.Assert(IrValidator.Validate(procedure).IsEmpty, "lowered IR must validate");
        return procedure;
    }

    /// <summary>
    /// Whether <paramref name="constructor"/>'s operation tree leaves out instance field or property initializers C# runs
    /// ahead of its body: it does not chain to <c>this(...)</c>, which runs them itself, and its type declares one.
    /// </summary>
    private static bool OmitsFieldInitializers(IMethodSymbol constructor, ConstructorDeclarationSyntax declaration) =>
        declaration.Initializer is not { RawKind: (int)SyntaxKind.ThisConstructorInitializer }
        && constructor.ContainingType.GetMembers()
            .Where(static m => !m.IsStatic)
            .SelectMany(static m => m.DeclaringSyntaxReferences)
            .Any(static r => r.GetSyntax() is VariableDeclaratorSyntax { Initializer: not null } or PropertyDeclarationSyntax { Initializer: not null });

    /// <summary>
    /// The C# parameters. One declared <c>@this</c> has the name <c>this</c>, which is the receiver's (ADR 0021), so it is
    /// spelled <c>$this</c>; no C# identifier contains <c>$</c>, so that name is never another parameter's (ticket M3-007).
    /// </summary>
    private static (ImmutableArray<IrParameter> Parameters, IrType? ReturnType) Signature(IMethodSymbol method, Func<string, string> sorts) => (
        [.. method.Parameters.Select(p => new IrParameter(
            new IrVar(IrParameterNames.IsSynthesised(p.Name) ? "$" + p.Name : p.Name, TypeMapper.Map(p.Type, sorts), p.Name),
            p.RefKind switch
            {
                RefKind.Ref => IrParameterKind.Ref,
                RefKind.Out => IrParameterKind.Out,
                _ => IrParameterKind.In,
            }))],
        method.ReturnsVoid ? null : TypeMapper.Map(method.ReturnType, sorts));

    /// <summary>
    /// Where <paramref name="syntax"/>'s bound code is erroneous (ADR 0029 decision 2): the span of every compiler error
    /// in it or, when there is none, of every <see cref="IInvalidOperation"/> and every operation of an error type in
    /// <paramref name="operation"/>, such as a reference to a field whose type did not resolve. Empty when it binds.
    /// </summary>
    private static ImmutableArray<SourceSpan> UnboundCauses(SyntaxNode syntax, SemanticModel model, IOperation? operation)
    {
        ImmutableArray<SourceSpan> errors =
        [
            .. model.GetDiagnostics(syntax.Span)
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(static d => CSharpFrontend.ToSourceSpan(d.Location))
                .Distinct(),
        ];
        ImmutableArray<SourceSpan> operations =
        [
            .. (operation?.DescendantsAndSelf() ?? [])
                .Where(static o => o is IInvalidOperation || o.Type is { TypeKind: TypeKind.Error })
                .Select(static o => Span(o.Syntax))
                .Distinct(),
        ];
        return errors.IsEmpty ? operations : errors;
    }

    /// <summary>
    /// One block: an opaque value (reason <paramref name="reason"/>) returned, by-ref parameters unchanged. There is one
    /// <see cref="IrOpaque"/> per span in <paramref name="spans"/>, the last one defining the value.
    /// </summary>
    private static IrProcedure Opaque(IMethodSymbol method, RenameMap renames, Catalogue catalogue, string reason, ImmutableArray<SourceSpan> spans)
    {
        (ImmutableArray<IrParameter> parameters, IrType? returnType) = Signature(method, catalogue.Sorts);
        IrVar? value = returnType is null ? null : new IrVar("$0", returnType);
        IrBlock block = new(
            new IrBlockId(0),
            [.. spans[..^1].Select(span => new IrOpaque(Target: null, reason, span)), new IrOpaque(value, reason, spans[^1])],
            new IrReturn(value, [.. parameters.Where(static p => p.Kind != IrParameterKind.In).Select(static p => new IrOut(p.Var, p.Var))]));
        return new IrProcedure(RoslynIdentity.Of(method, renames), parameters, returnType, [block], block.Id);
    }

    private static SourceSpan Span(SyntaxNode syntax) => CSharpFrontend.ToSourceSpan(syntax.GetLocation());

    private ImmutableArray<IrBlock> LowerBlocks(IMethodSymbol method, ImmutableArray<IrParameter> parameters, SourceSpan span)
    {
        bodySpan = span;
        receiver = method.ContainingType;
        chains = SwitchChains.Find(cfg);
        exceptions = new ExceptionLowerer(ssa, compilation, cfg, chains, bodySpan, Fill);
        heap = new HeapLowerer(ssa, Value, ThrowIfNull, Target, (context, condition, exceptionType) => ThrowIf(condition, exceptionType, context), catalogue.Sorts);
        assignedCaptures =
        [
            .. cfg.Blocks
                .SelectMany(static b => b.Operations.Append(b.BranchValue))
                .OfType<IOperation>()
                .SelectMany(static o => o.DescendantsAndSelf())
                .OfType<IAssignmentOperation>()
                .Select(static a => a.Target)
                .OfType<IFlowCaptureReferenceOperation>()
                .Select(static r => r.Id),
        ];
        // A `finally` is never lowered in place: it is copied onto each path that leaves its `try`.
        ImmutableArray<BasicBlock> reachable =
            [.. cfg.Blocks.Where(b => b.IsReachable && !chains.IsAbsorbed(b.Ordinal) && ExceptionRegions.EnclosingFinally(b) is null)];
        Dictionary<int, IrBlockId> blockIds = [];
        foreach (BasicBlock block in reachable)
        {
            blockIds[block.Ordinal] = ssa.NewBlock();
        }

        LoweringContext context = new(blockIds, blockIds, handlerExit: null);

        ImmutableArray<(SsaBuilder.Variable, IrVar)>.Builder outs = ImmutableArray.CreateBuilder<(SsaBuilder.Variable, IrVar)>();
        for (int i = 0; i < parameters.Length; i++)
        {
            SsaBuilder.Variable variable = Declare(method.Parameters[i], parameters[i].Var, method.Parameters[i].Type);
            ssa.Store(context.Current, variable, parameters[i].Var);
            if (Shadow(variable) is { } shadow)
            {
                ssa.Store(context.Current, shadow, heap.MapRead(heap.Inputs.Nulls((IrSort)parameters[i].Var.Type), parameters[i].Var, context));
            }

            if (parameters[i].Kind != IrParameterKind.In)
            {
                outs.Add((variable, parameters[i].Var));
            }
        }

        foreach (BasicBlock block in reachable)
        {
            Fill(block, context);
        }

        outs.AddRange(heap.Outs());
        return ssa.Build(new IrBlockId(0), outs.ToImmutable(), bodySpan);
    }

    private void Fill(BasicBlock block, LoweringContext context)
    {
        context.Source = block;
        context.Current = context.BlockIds[block.Ordinal];
        foreach (IOperation operation in block.Operations)
        {
            Statement(operation, context);
        }

        Terminate(block, context);
    }

    private void Terminate(BasicBlock block, LoweringContext context)
    {
        if (block.Kind == BasicBlockKind.Exit)
        {
            // Reached by a Regular fall-through: a void method's end, or erroneous code in a non-void one.
            if (returnType is null)
            {
                ssa.Terminate(context.Current, new IrReturn(Value: null, []));
            }
            else
            {
                OpaqueExit("missing-return", context);
            }

            return;
        }

        if (chains.Head(block.Ordinal) is { } chain)
        {
            Switch(chain, context);
            return;
        }

        ControlFlowBranch fallThrough = block.FallThroughSuccessor!;
        if (block.ConditionalSuccessor is { } conditional)
        {
            Branch(block, conditional, fallThrough, context);
            return;
        }

        switch (fallThrough.Semantics)
        {
            case ControlFlowBranchSemantics.Regular:
                ssa.Terminate(context.Current, new IrGoto(exceptions.Destination(fallThrough, context)));
                break;
            case ControlFlowBranchSemantics.Return:
                Return(Value(block.BranchValue!, context), fallThrough, context); // Value may move `context.Current` past exception edges
                break;
            case ControlFlowBranchSemantics.Throw when block.BranchValue is { } thrown:
                Throw(thrown, context);
                break;
            case ControlFlowBranchSemantics.StructuredExceptionHandling when context.HandlerExit is { } exit:
                ssa.Terminate(context.Current, new IrGoto(exit));
                break;
            default:
                // `throw;`: the exception in flight is not modelled. Also the edge erroneous code produces.
                OpaqueExit("rethrow", context);
                break;
        }
    }

    /// <summary>
    /// A two-way branch. <c>if (c) throw;</c> is one block whose fall-through is the rethrow, which names no
    /// block (ticket P2-010), so the rethrow gets a block of its own and is opaque as it is anywhere else.
    /// </summary>
    private void Branch(BasicBlock block, ControlFlowBranch conditional, ControlFlowBranch fallThrough, LoweringContext context)
    {
        IrVar condition = Value(block.BranchValue!, context);
        IrBlockId jump = exceptions.Destination(conditional, context);
        IrBlockId? rethrow = fallThrough.Semantics == ControlFlowBranchSemantics.Regular ? null : ssa.NewBlock();
        IrBlockId next = rethrow ?? exceptions.Destination(fallThrough, context);
        ssa.Terminate(context.Current, block.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? new IrBranch(condition, jump, next)
            : new IrBranch(condition, next, jump));
        if (rethrow is not null)
        {
            context.Current = rethrow;
            OpaqueExit("rethrow", context);
        }
    }

    /// <summary>A return runs every enclosing <c>finally</c> after evaluating its value and before exiting.</summary>
    private void Return(IrVar value, ControlFlowBranch branch, LoweringContext context)
    {
        if (branch.FinallyRegions.IsEmpty)
        {
            ssa.Terminate(context.Current, new IrReturn(value, []));
            return;
        }

        IrBlockId exit = ssa.NewBlock();
        ssa.Terminate(exit, new IrReturn(value, []));
        ssa.Terminate(context.Current, new IrGoto(exceptions.Unwind(branch.FinallyRegions, exit, context)));
    }

    /// <summary>
    /// <c>throw new T(...)</c> (ticket M2-004 acceptance criterion 3): the constructor call first, then
    /// <c>IrThrow("T")</c> on T's static type. Throwing anything else leaves the type unknown, so it is opaque.
    /// </summary>
    private void Throw(IOperation thrown, LoweringContext context)
    {
        IOperation value = thrown;
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        if (value is not IObjectCreationOperation creation)
        {
            OpaqueExit("Throw", context);
            return;
        }

        Lower(creation, context); // may move `context.Current` past the constructor's own threw branch
        ssa.Terminate(context.Current, new IrGoto(exceptions.Raise(TypeMapper.MetadataName(creation.Type!), creation.Type, context)));
    }

    /// <summary>A chain of equality tests on one scrutinee, folded back into one terminator (acceptance criterion 2).</summary>
    private void Switch(SwitchChains.Chain chain, LoweringContext context)
    {
        IrVar scrutinee = Value(chain.Scrutinee, context);
        // Every edge goes through Destination, so a case or the fall-out that leaves a `try` runs its
        // `finally` just as the branches this chain was folded from would have.
        ssa.Terminate(context.Current, new IrSwitch(
            scrutinee,
            [.. chain.Cases.Select(c => (TypeMapper.Constant(c.ConstantType, c.Constant), exceptions.Destination(c.Target, context)))],
            exceptions.Destination(chain.Default, context)));
    }

    /// <summary>
    /// A pattern test (acceptance criterion 2): a constant pattern is an equality, a discard is <c>true</c>,
    /// and every other pattern is opaque, which is how a pattern switch beyond constant cases stops here.
    /// </summary>
    private IrVar? Match(IIsPatternOperation pattern, LoweringContext context) => pattern.Pattern switch
    {
        IDiscardPatternOperation =>
            Const(new IrBoolValue(Value: true), context),
        IConstantPatternOperation { Value: { Type: { } type, ConstantValue: { HasValue: true, Value: { } constant } } }
            when TypeMapper.Map(type) is IrBitVec or IrBool && TypeMapper.Map(pattern.Value.Type!) == TypeMapper.Map(type) =>
            Emit(IrBinaryOp.Eq, Value(pattern.Value, context), Constant(type, constant, context), Bool, context),
        _ => Opaque(pattern, "switch-pattern", context),
    };

    private void OpaqueExit(string reason, LoweringContext context)
    {
        IrVar? value = returnType is null ? null : ssa.Temp(returnType);
        ssa.Emit(context.Current, new IrOpaque(value, reason, bodySpan));
        ssa.Terminate(context.Current, new IrReturn(value, []));
    }

    private void Statement(IOperation operation, LoweringContext context)
    {
        if (operation is IFlowCaptureOperation { Value: IPropertyReferenceOperation property } assigned && assignedCaptures.Contains(assigned.Id))
        {
            // The CFG captures a property an assignment writes when the value branches: its receiver and index
            // arguments are evaluated here, and its accessors run at the assignment (ticket M3-010).
            propertyTargets[assigned.Id] = new PropertyAccess(property, Operands(property.Instance, property.Arguments, context));
            return;
        }

        if (operation is IFlowCaptureOperation capture)
        {
            // A captured local or parameter may later be an assignment target (the CFG captures the
            // left-hand side first when the right-hand side branches), so remember it as an lvalue too.
            if (Target(capture.Value) is { } target)
            {
                captureTargets[capture.Id] = target;
            }

            IrVar value = Value(capture.Value, context);
            SsaBuilder.Variable captured = Capture(capture.Id, capture.Value.Type!);
            ssa.Store(context.Current, captured, value);
            StoreShadow(captured, capture.Value, value, context);
            return;
        }

        Lower(operation, context);
    }

    private IrVar Value(IOperation operation, LoweringContext context) => Lower(operation, context)!;

    /// <summary>The operation's value, or null for an operation without one (a statement, a void call).</summary>
    private IrVar? Lower(IOperation operation, LoweringContext context)
    {
        if (operation is { ConstantValue.HasValue: true, Type: { } constantType })
        {
            return Constant(constantType, operation.ConstantValue.Value, context);
        }

        switch (operation)
        {
            case IExpressionStatementOperation statement:
                Lower(statement.Operation, context);
                return null;
            case ILocalReferenceOperation local:
                return ssa.Load(context.Current, Local(local.Local));
            case IParameterReferenceOperation parameter when variables.TryGetValue(parameter.Parameter, out SsaBuilder.Variable? variable):
                return ssa.Load(context.Current, variable);
            case IFlowCaptureReferenceOperation reference:
                return ssa.Load(context.Current, Capture(reference.Id, reference.Type!));
            case ISimpleAssignmentOperation { IsRef: false } assignment:
                return Assign(assignment, context);
            case IFieldReferenceOperation field:
                return heap.ReadSlice(heap.Field(field, context), context);
            case IArrayElementReferenceOperation element:
                return heap.Element(element, context) is { } read ? heap.ReadSlice(read, context) : Opaque(element, element.Kind.ToString(), context);
            case IPropertyReferenceOperation property when heap.ArrayLength(property, context) is { } length:
                return length;
            case IPropertyReferenceOperation property:
                return Accessor(property, property.Property.GetMethod, Operands(property.Instance, property.Arguments, context), value: null, context);
            case IConversionOperation conversion:
                return Convert(conversion, context);
            case ITypeOfOperation typeOf when typeOf.TypeOperand is not ITypeParameterSymbol:
                return heap.Inputs.TypeOf(typeOf.TypeOperand, typeOf.Type!);
            case IBinaryOperation binary:
                return Binary(binary, context);
            case IUnaryOperation unary:
                return Unary(unary, context);
            case ICompoundAssignmentOperation compound:
                return Compound(compound, context);
            case IIncrementOrDecrementOperation step:
                return Step(step, context);
            case IInvocationOperation invocation:
                return Invoke(invocation, context);
            case IObjectCreationOperation creation:
                return Create(creation, context);
            case IIsPatternOperation pattern:
                return Match(pattern, context);
            case IIsNullOperation test when test.Operand.Type!.IsReferenceType:
                // The null test the CFG makes of a `using` resource or a `foreach` enumerator before disposing it.
                return NullFlag(test.Operand, Value(test.Operand, context), context);
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } when !receiver.IsValueType:
                // Of the containing type even where the reference is typed as the base, as in a `base(...)` initializer.
                return heap.Inputs.This(receiver);
            default:
                return Opaque(operation, operation.Kind.ToString(), context);
        }
    }

    private SsaBuilder.Variable Local(ILocalSymbol local) =>
        variables.TryGetValue(local, out SsaBuilder.Variable? variable)
            ? variable
            : Declare(local, new IrVar(local.Name, Map(local.Type), local.Name), local.Type);

    private SsaBuilder.Variable Capture(CaptureId id, ITypeSymbol type)
    {
        if (!captures.TryGetValue(id, out SsaBuilder.Variable? variable))
        {
            variable = Shadowed(new IrVar($"$c{captures.Count.ToString(CultureInfo.InvariantCulture)}", Map(type)), type);
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
    private IrVar? Nullness(IOperation source, IrVar value, LoweringContext context)
    {
        // A cast-map conversion's result is a value of its own, whose nullness is not tied to the operand's.
        IOperation unwrapped = source;
        while (unwrapped is IConversionOperation conversion && !IsCast(conversion))
        {
            unwrapped = conversion.Operand;
        }

        return unwrapped switch
        {
            IObjectCreationOperation or IInstanceReferenceOperation or ITypeOfOperation => null,
            _ when ShadowOf(unwrapped) is { } shadow => ssa.Load(context.Current, shadow),
            { ConstantValue.HasValue: true, ConstantValue.Value: null } => Const(new IrBoolValue(Value: true), context),
            _ => heap.MapRead(heap.Inputs.Nulls((IrSort)value.Type), value, context),
        };
    }

    /// <summary>Whether <paramref name="source"/> is null, as a value: <see cref="Nullness"/>, or false when it provably is not.</summary>
    private IrVar NullFlag(IOperation source, IrVar value, LoweringContext context) =>
        Nullness(source, value, context) ?? Const(new IrBoolValue(Value: false), context);

    /// <summary>Records the nullness of a value stored into a reference-typed variable.</summary>
    private void StoreShadow(SsaBuilder.Variable target, IOperation source, IrVar value, LoweringContext context)
    {
        if (Shadow(target) is { } shadow)
        {
            ssa.Store(context.Current, shadow, NullFlag(source, value, context));
        }
    }

    /// <summary>A dereference of a value that is not provably non-null throws <c>NullReferenceException</c> when it is.</summary>
    private void ThrowIfNull(IOperation source, IrVar value, LoweringContext context)
    {
        if (Nullness(source, value, context) is { } isNull)
        {
            ThrowIf(isNull, "System.NullReferenceException", context);
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
    /// An opaque for <paramref name="operation"/>, followed by one opaque with the same reason per local,
    /// parameter or capture it writes (ticket P2-009), so a later read sees a value and not <c>undefined</c>.
    /// </summary>
    private IrVar? Opaque(IOperation operation, string reason, LoweringContext context)
    {
        IrVar? target = operation.Type is { SpecialType: not SpecialType.System_Void } type ? ssa.Temp(Map(type)) : null;
        SourceSpan span = Span(operation.Syntax);
        ssa.Emit(context.Current, new IrOpaque(target, reason, span));
        foreach (SsaBuilder.Variable written in Written(operation))
        {
            IrVar value = ssa.Temp(written.Template.Type);
            ssa.Emit(context.Current, new IrOpaque(value, reason, span));
            ssa.Store(context.Current, written, value);
            if (Shadow(written) is { } shadow)
            {
                ssa.Store(context.Current, shadow, heap.MapRead(heap.Inputs.Nulls((IrSort)value.Type), value, context));
            }
        }

        return target;
    }

    /// <summary>The variables an operation writes as a side effect: its <c>ref</c>/<c>out</c> arguments, deconstruction targets and pattern-declared locals.</summary>
    private IEnumerable<SsaBuilder.Variable> Written(IOperation operation) =>
        operation.DescendantsAndSelf()
            .SelectMany(o => o switch
            {
                IArgumentOperation argument when argument.Parameter!.RefKind is RefKind.Ref or RefKind.Out => Lvalues(argument.Value).Select(Target),
                IDeconstructionAssignmentOperation deconstruction => Lvalues(deconstruction.Target).Select(Target),
                IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol local } => [Local(local)],
                _ => [],
            })
            .OfType<SsaBuilder.Variable>()
            .Distinct();

    /// <summary>The lvalues a (possibly declared, possibly tuple) target names.</summary>
    private static IEnumerable<IOperation> Lvalues(IOperation target) => target switch
    {
        IDeclarationExpressionOperation declaration => Lvalues(declaration.Expression),
        ITupleOperation tuple => tuple.Elements.SelectMany(Lvalues),
        _ => [target],
    };

    private IrVar Constant(ITypeSymbol type, object? value, LoweringContext context) => Const(TypeMapper.Constant(type, value, catalogue.Sorts), context);

    /// <summary>The IR type of <paramref name="type"/>, with this side's sort names (ticket M3-009).</summary>
    private IrType Map(ITypeSymbol type) => TypeMapper.Map(type, catalogue.Sorts);

    private IrVar Const(IrValue value, LoweringContext context)
    {
        IrVar target = ssa.Temp(value.Type);
        ssa.Emit(context.Current, new IrConst(target, value));
        return target;
    }

    private IrVar Zero(IrType type, LoweringContext context) => Const(new IrBitVecValue(((IrBitVec)type).Width, 0), context);

    private IrVar Emit(IrBinaryOp op, IrVar left, IrVar right, IrType type, LoweringContext context)
    {
        IrVar target = ssa.Temp(type);
        ssa.Emit(context.Current, new IrBinary(target, op, left, right));
        return target;
    }

    /// <summary>
    /// Ends the current block with a branch, taken when <paramref name="condition"/> holds, to wherever an
    /// exception of <paramref name="exceptionType"/> goes from here: a matching <c>catch</c> or the shared
    /// throw block, behind any <c>finally</c> it leaves. A null <paramref name="type"/> means the type is
    /// not known, which is the case for an opaque call's <c>threw</c> flag.
    /// </summary>
    private void ThrowIf(IrVar condition, string exceptionType, LoweringContext context, bool known = true)
    {
        IrBlockId thrown = exceptions.Raise(exceptionType, known ? compilation.GetTypeByMetadataName(exceptionType) : null, context);
        IrBlockId next = ssa.NewBlock();
        ssa.Terminate(context.Current, new IrBranch(condition, thrown, next));
        context.Current = next;
    }

    private void ThrowIfOverflows(IrOverflowOp op, IrVar left, IrVar right, LoweringContext context)
    {
        IrVar overflows = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrOverflows(overflows, op, left, right));
        ThrowIf(overflows, OverflowException, context);
    }

    private IrVar? Assign(ISimpleAssignmentOperation assignment, LoweringContext context)
    {
        if (heap.Slice(assignment.Target, context) is { } slice)
        {
            // C# evaluates the target's receiver and index, then the value, and only then stores, so
            // the null and bounds checks (made at the store) come after the value (ticket P2-017).
            IrVar written = Value(assignment.Value, context);
            heap.WriteSlice(slice, written, context);
            return written;
        }

        if (PropertyTarget(assignment.Target, context) is { } property)
        {
            IrVar assigned = Value(assignment.Value, context);
            Accessor(property.Reference, Setter(property.Reference.Property), property.Operands, assigned, context);
            return assigned;
        }

        if (Target(assignment.Target) is not { } target)
        {
            return Opaque(assignment, assignment.Target.Kind.ToString(), context);
        }

        IrVar value = Value(assignment.Value, context);
        ssa.Store(context.Current, target, value);
        StoreShadow(target, assignment.Value, value, context);
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

    /// <summary>
    /// An implicit reference or boxing conversion between different IR types is a read of its <c>cast.&lt;From&gt;.&lt;To&gt;</c>
    /// map (ticket M3-010), and an identity conversion is its operand (ticket M4-001). Otherwise integral to integral only: extension follows the source's signedness; a checked
    /// narrowing throws when the value does not fit.
    /// </summary>
    private IrVar? Convert(IConversionOperation conversion, LoweringContext context)
    {
        if (IsCast(conversion))
        {
            return heap.MapRead(heap.Inputs.Cast(conversion.Operand.Type!, conversion.Type!), Value(conversion.Operand, context), context);
        }

        if (conversion.GetConversion().IsIdentity)
        {
            // Such as the one the CFG wraps around a `foreach` collection.
            return Value(conversion.Operand, context);
        }

        if (conversion.OperatorMethod is not null
            || conversion.Operand.Type is not { } from
            || TypeMapper.Map(from) is not IrBitVec
            || TypeMapper.Map(conversion.Type!) is not IrBitVec target)
        {
            return Opaque(conversion, conversion.Kind.ToString(), context);
        }

        bool fromSigned = TypeMapper.IsSigned(from);
        bool toSigned = TypeMapper.IsSigned(conversion.Type!);
        IrVar value = Value(conversion.Operand, context);
        IrVar result = Resize(value, target, fromSigned, context);
        if (conversion.IsChecked && !conversion.Conversion.IsImplicit)
        {
            ThrowIfItDoesNotFit(value, result, fromSigned, toSigned, context);
        }

        return result;
    }

    /// <summary>Whether <paramref name="conversion"/> is an implicit reference or boxing conversion that changes the IR type.</summary>
    private static bool IsCast(IConversionOperation conversion) =>
        conversion.GetConversion() is { IsImplicit: true } kind
        && (kind.IsReference || kind.IsBoxing)
        && conversion.Operand.Type is { } from // the null literal's reference conversion has no source type
        && TypeMapper.Map(from) != TypeMapper.Map(conversion.Type!);

    /// <summary>Throws when <paramref name="result"/> does not round-trip to <paramref name="value"/>, or when the signed side of a signedness change is negative.</summary>
    private void ThrowIfItDoesNotFit(IrVar value, IrVar result, bool fromSigned, bool toSigned, LoweringContext context)
    {
        IrVar lost = Emit(IrBinaryOp.Ne, Resize(result, (IrBitVec)value.Type, toSigned, context), value, Bool, context);
        if (fromSigned != toSigned)
        {
            IrVar signedSide = fromSigned ? value : result;
            lost = Emit(IrBinaryOp.Or, lost, Emit(IrBinaryOp.Slt, signedSide, Zero(signedSide.Type, context), Bool, context), Bool, context);
        }

        ThrowIf(lost, OverflowException, context);
    }

    private IrVar Resize(IrVar value, IrBitVec type, bool signed, LoweringContext context)
    {
        int width = ((IrBitVec)value.Type).Width;
        if (width == type.Width)
        {
            return value;
        }

        IrVar target = ssa.Temp(type);
        IrUnaryOp op = width switch
        {
            _ when width >= type.Width => IrUnaryOp.Trunc,
            _ when signed => IrUnaryOp.SExt,
            _ => IrUnaryOp.ZExt,
        };
        ssa.Emit(context.Current, new IrUnary(target, op, value));
        return target;
    }

    private IrVar? Binary(IBinaryOperation binary, LoweringContext context)
    {
        if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals && NullTest(binary, context) is { } test)
        {
            return test;
        }

        if (binary is { OperatorKind: BinaryOperatorKind.Add, LeftOperand.Type.SpecialType: SpecialType.System_String, RightOperand.Type.SpecialType: SpecialType.System_String })
        {
            // Strings stay uninterpreted, so `a + b` is the call the compiler makes.
            return Call(
                new CallIdentity(ProcedureIdentityNormalizer.Member("System", "String", "Concat", 0, ["string", "string"], renames).Value),
                [Value(binary.LeftOperand, context), Value(binary.RightOperand, context)],
                Map(binary.Type!),
                context);
        }

        bool signed = TypeMapper.IsSigned(binary.LeftOperand.Type!);
        IrVar left = Value(binary.LeftOperand, context);
        IrVar right = Value(binary.RightOperand, context);
        return OperatorMapper.Binary(binary.OperatorKind, signed, left.Type, right.Type) switch
        {
            not { } => Opaque(binary, binary.Kind.ToString(), context),
            { } op when OperatorMapper.IsShift(op) => Shift(op, left, right, (IrBitVec)left.Type, context),
            { } op => Arithmetic(op, left, right, signed, binary.IsChecked, Map(binary.Type!), context),
        };
    }

    /// <summary><c>x == null</c> and <c>x != null</c> compare the shadow (acceptance criterion 5); null when neither side is <c>null</c>.</summary>
    private IrVar? NullTest(IBinaryOperation binary, LoweringContext context)
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

        IrVar isNull = NullFlag(other, Value(other, context), context);
        return binary.OperatorKind == BinaryOperatorKind.Equals ? isNull : EmitUnary(IrUnaryOp.BoolNot, isNull, context);
    }

    private static bool IsNull(IOperation operand) => Unwrap(operand).ConstantValue is { HasValue: true, Value: null };

    /// <summary>C# masks the shift count to the left operand's width (ECMA-334 shift operators); the IR shift does not.</summary>
    private IrVar Shift(IrBinaryOp op, IrVar left, IrVar count, IrBitVec type, LoweringContext context)
    {
        IrVar mask = Const(new IrBitVecValue(((IrBitVec)count.Type).Width, (ulong)type.Width - 1), context);
        IrVar masked = Resize(Emit(IrBinaryOp.And, count, mask, count.Type, context), type, signed: false, context);
        return Emit(op, left, masked, type, context);
    }

    private IrVar Arithmetic(IrBinaryOp op, IrVar left, IrVar right, bool signed, bool isChecked, IrType type, LoweringContext context)
    {
        if (op is IrBinaryOp.SDiv or IrBinaryOp.SRem or IrBinaryOp.UDiv or IrBinaryOp.URem)
        {
            ThrowIf(Emit(IrBinaryOp.Eq, right, Zero(right.Type, context), Bool, context), "System.DivideByZeroException", context);
            if (signed)
            {
                // .NET throws on MinValue / -1 and MinValue % -1 in unchecked code too.
                ThrowIfOverflows(IrOverflowOp.SDiv, left, right, context);
            }
        }
        else if (isChecked && OperatorMapper.Overflow(op, signed) is { } overflow)
        {
            ThrowIfOverflows(overflow, left, right, context);
        }

        return Emit(op, left, right, type, context);
    }

    /// <summary>
    /// <c>x op= v</c> (ticket M2-004 acceptance criterion 9): read, promote to the operator's type, operate
    /// with the binary operator's exception edges, narrow back to the target type, write. A shift takes its
    /// operator type from the promoted target; every other operator from the right operand, which Roslyn has
    /// already converted to it.
    /// </summary>
    private IrVar? Compound(ICompoundAssignmentOperation compound, LoweringContext context)
    {
        ITypeSymbol right = compound.Value.Type!;
        (IrBitVec Type, bool Signed)? mappedRight = TypeMapper.Map(right) is IrBitVec bits ? (bits, TypeMapper.IsSigned(right)) : null;
        (IrBitVec Type, bool Signed)? operands = OperatorMapper.IsShiftKind(compound.OperatorKind)
            ? TypeMapper.Promote(compound.Target.Type!)
            : mappedRight;
        return compound.OperatorMethod is null && operands is { } promoted
            ? Update(new UpdateSite(compound, compound.Target, compound.IsChecked, IsPostfix: false), compound.OperatorKind, () => Value(compound.Value, context), promoted, context)
            : Opaque(compound, compound.Kind.ToString(), context);
    }

    /// <summary><c>x++</c>, <c>--x</c>: the right operand is a promoted <c>1</c>; postfix yields the value read.</summary>
    private IrVar? Step(IIncrementOrDecrementOperation step, LoweringContext context)
    {
        if (TypeMapper.Promote(step.Type!) is not { } promoted)
        {
            return Opaque(step, step.Kind.ToString(), context);
        }

        BinaryOperatorKind kind = step.Kind == OperationKind.Increment ? BinaryOperatorKind.Add : BinaryOperatorKind.Subtract;
        return Update(new UpdateSite(step, step.Target, step.IsChecked, step.IsPostfix), kind, () => Const(new IrBitVecValue(promoted.Type.Width, 1), context), promoted, context);
    }

    /// <summary>
    /// Reads the target, evaluates <paramref name="operand"/> (C# reads a compound assignment's target first), operates
    /// and writes back. The target is a local or parameter, or a property with a getter and a non-init setter, whose
    /// receiver and index arguments are evaluated once for both accessor calls (ticket M3-010 acceptance criterion 2).
    /// </summary>
    private IrVar? Update(UpdateSite site, BinaryOperatorKind kind, Func<IrVar> operand, (IrBitVec Type, bool Signed) promoted, LoweringContext context)
    {
        (IOperation node, IOperation lvalue, bool isChecked, bool isPostfix) = site;
        if (TypeMapper.Map(lvalue.Type!) is not IrBitVec narrow || Place(lvalue, context) is not { } place)
        {
            return Opaque(node, lvalue.Kind.ToString(), context);
        }

        (Func<IrVar> read, Action<IrVar> write) = place;
        IrVar old = read();
        IrVar right = operand();
        // Never null here: a shift takes operands of any two widths, and every other compound operator
        // was given the right operand's own bitvector type.
        IrBinaryOp op = OperatorMapper.Binary(kind, promoted.Signed, promoted.Type, right.Type)!.Value;
        bool targetSigned = TypeMapper.IsSigned(lvalue.Type!);
        IrVar wide = Resize(old, promoted.Type, targetSigned, context);
        IrVar computed = OperatorMapper.IsShift(op)
            ? Shift(op, wide, right, promoted.Type, context)
            : Arithmetic(op, wide, right, promoted.Signed, isChecked, promoted.Type, context);
        IrVar result = Resize(computed, narrow, promoted.Signed, context);
        if (isChecked && narrow != promoted.Type)
        {
            ThrowIfItDoesNotFit(computed, result, promoted.Signed, targetSigned, context);
        }

        write(result);
        return isPostfix ? old : result;
    }

    /// <summary>
    /// How to read and write an lvalue that is both read and written, or null, with nothing emitted, when it is neither
    /// a variable nor a property. A property's receiver and index arguments are evaluated here, once, for both accessors.
    /// </summary>
    private (Func<IrVar> Read, Action<IrVar> Write)? Place(IOperation lvalue, LoweringContext context)
    {
        if (Target(lvalue) is { } target)
        {
            return (() => ssa.Load(context.Current, target), value => ssa.Store(context.Current, target, value));
        }

        return PropertyTarget(lvalue, context) is { } property
            ? (() => Accessor(property.Reference, property.Reference.Property.GetMethod, property.Operands, value: null, context)!,
                value => Accessor(property.Reference, Setter(property.Reference.Property), property.Operands, value, context))
            : null;
    }

    /// <summary>
    /// The property an lvalue writes, with its receiver and index arguments evaluated now, or, for a captured one, when
    /// it was captured; null, with nothing emitted, when the lvalue is not a property.
    /// </summary>
    private PropertyAccess? PropertyTarget(IOperation lvalue, LoweringContext context) => lvalue switch
    {
        IPropertyReferenceOperation property => new PropertyAccess(property, Operands(property.Instance, property.Arguments, context)),
        IFlowCaptureReferenceOperation reference when propertyTargets.TryGetValue(reference.Id, out PropertyAccess? captured) => captured,
        _ => null,
    };

    private IrVar? Unary(IUnaryOperation unary, LoweringContext context)
    {
        IrVar operand = Value(unary.Operand, context);
        switch (unary.OperatorKind, operand.Type)
        {
            case (UnaryOperatorKind.Not, IrBool):
                return EmitUnary(IrUnaryOp.BoolNot, operand, context);
            case (UnaryOperatorKind.BitwiseNegation, IrBitVec):
                return EmitUnary(IrUnaryOp.Not, operand, context);
            case (UnaryOperatorKind.Minus, IrBitVec):
                if (unary.IsChecked)
                {
                    ThrowIfOverflows(IrOverflowOp.SSub, Zero(operand.Type, context), operand, context);
                }

                return EmitUnary(IrUnaryOp.Neg, operand, context);
            case (UnaryOperatorKind.Plus, IrBitVec):
                return operand;
            default:
                return Opaque(unary, unary.Kind.ToString(), context);
        }
    }

    private IrVar EmitUnary(IrUnaryOp op, IrVar operand, LoweringContext context)
    {
        IrVar target = ssa.Temp(operand.Type);
        ssa.Emit(context.Current, new IrUnary(target, op, operand));
        return target;
    }

    /// <summary><c>new T(...)</c>: an opaque call to the constructor yielding the new object.</summary>
    private IrVar? Create(IObjectCreationOperation creation, LoweringContext context) =>
        creation.Arguments.Any(static a => a.Parameter!.RefKind is RefKind.Ref or RefKind.Out)
            ? Opaque(creation, "ref-argument", context)
            : Call(Identity(creation.Constructor!), [.. Arguments([], creation.Arguments, context)], Map(creation.Type!), context);

    /// <summary>
    /// An opaque call (receiver first, then arguments in parameter order) that may throw System.Exception. A call to an
    /// API-equivalence entry's legacy member whose arguments its adapter addresses is a call to the entry's modern member
    /// instead, and the entry is recorded as applied (ADR 0020; ticket M3-009).
    /// </summary>
    private IrVar? Invoke(IInvocationOperation invocation, LoweringContext context)
    {
        if (invocation.Arguments.Any(static a => a.Parameter!.RefKind is RefKind.Ref or RefKind.Out))
        {
            return Opaque(invocation, "ref-argument", context);
        }

        CallIdentity callee = Identity(invocation.TargetMethod);
        IrType? returns = invocation.TargetMethod.ReturnsVoid ? null : Map(invocation.Type!);
        if (catalogue.Members.TryGetValue(callee.Value, out ApiEquivalence? entry) && Adapt(entry, invocation, context) is { } adapted)
        {
            catalogue.Applied.Add(entry.Id);
            return Call(CallIdentityFactory.Of(entry.Modern, suppressedRuntimeChanges), adapted, returns, context);
        }

        return Dispatch(invocation.Instance, callee, Operands(invocation.Instance, invocation.Arguments, context), returns, context);
    }

    /// <summary>
    /// The modern call's arguments under <paramref name="entry"/>'s adapter, or null, with nothing emitted, when it cannot
    /// address the legacy call's source arguments (<see cref="Plan"/>). The source arguments are evaluated in source order,
    /// and then the receiver of an instance call is null-checked, as <see cref="Dispatch"/> checks the legacy call's
    /// (ticket P2-017); a static or extension call has no check, even when the modern member is an instance member (ADR 0020).
    /// </summary>
    private ImmutableArray<IrVar>? Adapt(ApiEquivalence entry, IInvocationOperation invocation, LoweringContext context)
    {
        if (CallIdentityFactory.SourceArguments(invocation.Instance, invocation.Arguments) is not { } sources
            || Plan(entry.Arguments, [.. sources.OrderBy(static s => s.Position).Select(static s => s.Value)]) is not { } plan)
        {
            return null;
        }

        IrVar[] values = new IrVar[sources.Length];
        foreach ((int position, _) in sources)
        {
            values[position] = Value(plan.Operands[position], context);
        }

        ImmutableArray<IrVar> adapted =
        [
            .. entry.Arguments.Select((item, i) => item.Source is { } source
                ? Adapted(plan.Operands[source], values[source], plan.Targets[i], context)
                : Const(TypeMapper.Constant(item.ConstantType!, item.Constant!)!, context)),
        ];
        if (invocation.Instance is { Type.IsValueType: false } instance)
        {
            ThrowIfNull(instance, values[0], context);
        }

        return adapted;
    }

    /// <summary>
    /// What each adapter item takes, checked before anything is emitted: every source position is in range and used, so
    /// a <c>params</c> element count other than the entry's does not match; an unwrapped argument has an implicit
    /// conversion and is not also used as it is; a <c>convertTo</c> type resolves and the conversion to it is an implicit
    /// identity, boxing or reference conversion; a constant parses. Null when any of that fails.
    /// </summary>
    private AdapterPlan? Plan(ImmutableArray<ApiArgument> items, ImmutableArray<IOperation> sources)
    {
        IOperation[] operands = [.. sources];
        bool?[] unwrapped = new bool?[sources.Length];
        ImmutableArray<ITypeSymbol?>.Builder targets = ImmutableArray.CreateBuilder<ITypeSymbol?>(items.Length);
        foreach (ApiArgument item in items)
        {
            if (!Planned(item, sources, operands, unwrapped, out ITypeSymbol? target))
            {
                return null;
            }

            targets.Add(target);
        }

        return Array.TrueForAll(unwrapped, static u => u is not null) ? new AdapterPlan([.. operands], targets.MoveToImmutable()) : null;
    }

    /// <summary>
    /// One adapter item of <see cref="Plan"/>: false when it cannot be addressed. A source item records whether its
    /// argument is unwrapped in <paramref name="unwrapped"/> and, when it is, the conversion's operand in
    /// <paramref name="operands"/>; <paramref name="target"/> is its <c>convertTo</c> type, or null.
    /// </summary>
    private bool Planned(ApiArgument item, ImmutableArray<IOperation> sources, IOperation[] operands, bool?[] unwrapped, out ITypeSymbol? target)
    {
        target = null;
        if (item.Source is not { } position)
        {
            return TypeMapper.Constant(item.ConstantType!, item.Constant!) is not null;
        }

        if ((uint)position >= (uint)sources.Length || (unwrapped[position] ?? item.Unwrap) != item.Unwrap)
        {
            return false;
        }

        unwrapped[position] = item.Unwrap;
        if (item.Unwrap)
        {
            if (sources[position] is not IConversionOperation conversion || !conversion.GetConversion().IsImplicit)
            {
                return false;
            }

            operands[position] = conversion.Operand;
        }

        if (item.ConvertTo is not { } name)
        {
            return true;
        }

        target = compilation.GetTypeByMetadataName(name);
        return target is not null && IsImplicitCast(operands[position].Type, target);
    }

    /// <summary>Whether a value of <paramref name="from"/> converts to <paramref name="to"/> by an implicit identity, boxing or reference conversion.</summary>
    private bool IsImplicitCast(ITypeSymbol? from, ITypeSymbol to) =>
        from is not null
        && compilation.ClassifyConversion(from, to) is { IsImplicit: true } conversion
        && (conversion.IsIdentity || conversion.IsBoxing || conversion.IsReference);

    /// <summary>An adapted source argument as it is or, converted to <paramref name="target"/>, a read of the <c>cast</c> map, as M3-010 lowers that conversion.</summary>
    private IrVar Adapted(IOperation operand, IrVar value, ITypeSymbol? target, LoweringContext context) =>
        target is null || Map(operand.Type!) == Map(target)
            ? value
            : heap.MapRead(heap.Inputs.Cast(operand.Type!, target), value, context);

    /// <summary>
    /// A property access is a call to its accessor, lowered as an invocation of it is (ticket M3-010 acceptance criteria
    /// 1 to 3): the getter with the receiver and index arguments, or, given <paramref name="value"/>, the setter with the
    /// value last and no result. A property with no accessor for the access stays opaque.
    /// </summary>
    private IrVar? Accessor(IPropertyReferenceOperation property, IMethodSymbol? accessor, ImmutableArray<IrVar> operands, IrVar? value, LoweringContext context)
    {
        if (accessor is null)
        {
            return Opaque(property, property.Kind.ToString(), context);
        }

        IrType? returns = value is null ? Map(property.Type!) : null;
        return Dispatch(property.Instance, Identity(accessor), value is null ? operands : [.. operands, value], returns, context);
    }

    /// <summary>The setter an assignment calls; an init-only one is callable only from an initializer, which is not lowered.</summary>
    private static IMethodSymbol? Setter(IPropertySymbol property) => property.SetMethod is { IsInitOnly: false } setter ? setter : null;

    private CallIdentity Identity(IMethodSymbol method) => CallIdentityFactory.Of(method, renames, suppressedRuntimeChanges);

    /// <summary>
    /// A member access's call operands: the receiver, then the arguments. The receiver is null-checked at the call, by
    /// <see cref="Dispatch"/>, not here.
    /// </summary>
    private ImmutableArray<IrVar> Operands(IOperation? instance, ImmutableArray<IArgumentOperation> arguments, LoweringContext context) =>
        [.. Arguments(instance is null ? [] : [Value(instance, context)], arguments, context)];

    /// <summary>
    /// A call through <paramref name="receiver"/>, whose value is <paramref name="args"/>' first when it is not null: like
    /// <c>callvirt</c>, it null-checks a receiver of a reference type at the call, after every argument, a setter's value
    /// included (ticket P2-017).
    /// </summary>
    private IrVar? Dispatch(IOperation? receiver, CallIdentity callee, ImmutableArray<IrVar> args, IrType? returns, LoweringContext context)
    {
        if (receiver is not null && !receiver.Type!.IsValueType)
        {
            ThrowIfNull(receiver, args[0], context);
        }

        return Call(callee, args, returns, context);
    }

    /// <summary>The receiver, then the arguments in parameter order; each is evaluated in source order first.</summary>
    private IEnumerable<IrVar> Arguments(IEnumerable<IrVar> receiver, ImmutableArray<IArgumentOperation> arguments, LoweringContext context) =>
        receiver.Concat(arguments
            .Select(a => (a.Parameter!.Ordinal, Value: Value(a.Value, context)))
            .OrderBy(static a => a.Ordinal)
            .Select(static a => a.Value));

    private IrVar? Call(CallIdentity callee, ImmutableArray<IrVar> args, IrType? returns, LoweringContext context)
    {
        IrVar? target = returns is null ? null : ssa.Temp(returns);
        IrVar threw = ssa.Temp(Bool);
        ssa.Emit(context.Current, new IrCall(target, threw, callee, args));
        ThrowIf(threw, "System.Exception", context, known: false);
        return target;
    }

    /// <summary>The operation <see cref="Update"/> rewrites, the lvalue it reads and writes, and how: checked, and whether it yields the value read.</summary>
    private sealed record UpdateSite(IOperation Node, IOperation Target, bool IsChecked, bool IsPostfix);

    /// <summary>A property an assignment writes, and its receiver and index arguments, evaluated once.</summary>
    private sealed record PropertyAccess(IPropertyReferenceOperation Reference, ImmutableArray<IrVar> Operands);

    /// <summary>Each source argument's operand as an adapter takes it, and each adapter item's <c>convertTo</c> type, if any.</summary>
    private sealed record AdapterPlan(ImmutableArray<IOperation> Operands, ImmutableArray<ITypeSymbol?> Targets);

    /// <summary>
    /// The API-equivalence entries one body is lowered with (ADR 0020; ticket M3-009): member entries by legacy identity,
    /// type entries as <see cref="Sorts"/>, and the ids of the entries that fired, sorted.
    /// </summary>
    private sealed class Catalogue
    {
        private readonly ImmutableDictionary<string, ApiEquivalence> types;

        public Catalogue(ImmutableArray<ApiEquivalence> entries)
        {
            Members = entries.Where(static e => !e.IsType).ToImmutableDictionary(static e => e.Legacy, StringComparer.Ordinal);
            types = entries.Where(static e => e.IsType).ToImmutableDictionary(static e => e.Legacy, StringComparer.Ordinal);
            Sorts = Sort;
        }

        public ImmutableDictionary<string, ApiEquivalence> Members { get; }

        public SortedSet<string> Applied { get; } = new(StringComparer.Ordinal);

        /// <summary>A sort name, mapped by a type entry, which is then recorded as applied, or left as it is.</summary>
        public Func<string, string> Sorts { get; }

        private string Sort(string name)
        {
            if (!types.TryGetValue(name, out ApiEquivalence? entry))
            {
                return name;
            }

            Applied.Add(entry.Id);
            return entry.Modern;
        }
    }
}
