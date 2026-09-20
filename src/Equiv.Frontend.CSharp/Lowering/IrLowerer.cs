using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
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
    private readonly Dictionary<int, IrBlockId> blockIds = [];
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
        ControlFlowGraph cfg = ControlFlowGraph.Create(body);
        SourceSpan span = Span(body.Syntax);
        string? wholeBody = body.Descendants().Any(static o => o is ILoopOperation) ? "loop"
            : body.Descendants().Any(static o => o is ISwitchOperation or ISwitchExpressionOperation) ? "switch"
            : HasExceptionRegion(cfg.Root) ? "try-region"
            : null;
        if (wholeBody is not null)
        {
            return Opaque(method, renames, wholeBody, span);
        }

        (ImmutableArray<IrParameter> parameters, IrType? returnType) = Signature(method);
        IrLowerer lowerer = new(renames, returnType);
        IrProcedure procedure = new(
            RoslynIdentity.Of(method, renames),
            parameters,
            returnType,
            lowerer.LowerBlocks(cfg, method, parameters, span),
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

    /// <summary>Every exception region nests a <c>TryAndCatch</c> or <c>TryAndFinally</c> region.</summary>
    private static bool HasExceptionRegion(ControlFlowRegion region) =>
        region.Kind is ControlFlowRegionKind.TryAndCatch or ControlFlowRegionKind.TryAndFinally || region.NestedRegions.Any(HasExceptionRegion);

    private static SourceSpan Span(SyntaxNode syntax) => CSharpFrontend.ToSourceSpan(syntax.GetLocation());

    private ImmutableArray<IrBlock> LowerBlocks(ControlFlowGraph cfg, IMethodSymbol method, ImmutableArray<IrParameter> parameters, SourceSpan span)
    {
        ImmutableArray<BasicBlock> reachable = [.. cfg.Blocks.Where(static b => b.IsReachable)];
        foreach (BasicBlock block in reachable)
        {
            blockIds[block.Ordinal] = ssa.NewBlock();
        }

        ImmutableArray<(SsaBuilder.Variable, IrVar)>.Builder outs = ImmutableArray.CreateBuilder<(SsaBuilder.Variable, IrVar)>();
        for (int i = 0; i < parameters.Length; i++)
        {
            SsaBuilder.Variable variable = new(parameters[i].Var);
            variables[method.Parameters[i]] = variable;
            ssa.Store(current, variable, parameters[i].Var);
            if (parameters[i].Kind != IrParameterKind.In)
            {
                outs.Add((variable, parameters[i].Var));
            }
        }

        foreach (BasicBlock block in reachable)
        {
            current = blockIds[block.Ordinal];
            foreach (IOperation operation in block.Operations)
            {
                Statement(operation);
            }

            Terminate(block, span);
        }

        return ssa.Build(new IrBlockId(0), outs.ToImmutable(), span);
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

        ControlFlowBranch fallThrough = block.FallThroughSuccessor!;
        if (block.ConditionalSuccessor is { } conditional)
        {
            IrVar condition = Value(block.BranchValue!);
            IrBlockId jump = blockIds[conditional.Destination!.Ordinal];
            IrBlockId next = blockIds[fallThrough.Destination!.Ordinal];
            ssa.Terminate(current, block.ConditionKind == ControlFlowConditionKind.WhenTrue
                ? new IrBranch(condition, jump, next)
                : new IrBranch(condition, next, jump));
            return;
        }

        switch (fallThrough.Semantics)
        {
            case ControlFlowBranchSemantics.Regular:
                ssa.Terminate(current, new IrGoto(blockIds[fallThrough.Destination!.Ordinal]));
                break;
            case ControlFlowBranchSemantics.Return:
                IrVar value = Value(block.BranchValue!); // may move `current` past overflow and call-threw branches
                ssa.Terminate(current, new IrReturn(value, []));
                break;
            default:
                // Throw (the thrown object's dynamic type is not known statically), or an edge only erroneous code has.
                OpaqueExit(fallThrough.Semantics.ToString(), span);
                break;
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
            ssa.Store(current, Capture(capture.Id, value.Type), value);
            return;
        }

        Lower(operation);
    }

    private IrVar Value(IOperation operation) => Lower(operation)!;

    /// <summary>The operation's value, or null for an operation without one (a statement, a void call).</summary>
    private IrVar? Lower(IOperation operation)
    {
        if (operation.ConstantValue is { HasValue: true, Value: { } constant } && TypeMapper.Map(operation.Type!) is not IrSort)
        {
            return Constant(operation.Type!, constant);
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
                return ssa.Load(current, Capture(reference.Id, TypeMapper.Map(reference.Type!)));
            case ISimpleAssignmentOperation { IsRef: false } assignment:
                return Assign(assignment);
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
            default:
                return Opaque(operation, operation.Kind.ToString());
        }
    }

    private SsaBuilder.Variable Local(ILocalSymbol local)
    {
        if (!variables.TryGetValue(local, out SsaBuilder.Variable? variable))
        {
            variable = new SsaBuilder.Variable(new IrVar(local.Name, TypeMapper.Map(local.Type), local.Name));
            variables[local] = variable;
        }

        return variable;
    }

    private SsaBuilder.Variable Capture(CaptureId id, IrType type)
    {
        if (!captures.TryGetValue(id, out SsaBuilder.Variable? variable))
        {
            variable = new SsaBuilder.Variable(new IrVar($"$c{captures.Count.ToString(CultureInfo.InvariantCulture)}", type));
            captures[id] = variable;
        }

        return variable;
    }

    private IrVar? Opaque(IOperation operation, string reason)
    {
        IrVar? target = operation.Type is { SpecialType: not SpecialType.System_Void } type ? ssa.Temp(TypeMapper.Map(type)) : null;
        ssa.Emit(current, new IrOpaque(target, reason, Span(operation.Syntax)));
        return target;
    }

    private IrVar Constant(ITypeSymbol type, object value)
    {
        IrType irType = TypeMapper.Map(type);
        IrValue irValue = irType switch
        {
            IrBitVec bits when TypeMapper.IsSigned(type) => IrBitVecValue.FromSigned(bits.Width, System.Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            IrBitVec bits => new IrBitVecValue(bits.Width, System.Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            _ => new IrBoolValue((bool)value),
        };
        return Const(irValue);
    }

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

    /// <summary>Ends the current block with a branch to the shared <paramref name="exceptionType"/> throw block when <paramref name="condition"/> holds.</summary>
    private void ThrowIf(IrVar condition, string exceptionType)
    {
        if (!throwBlocks.TryGetValue(exceptionType, out IrBlockId? thrown))
        {
            thrown = ssa.NewBlock();
            ssa.Terminate(thrown, new IrThrow(exceptionType, []));
            throwBlocks[exceptionType] = thrown;
        }

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
        if (Target(assignment.Target) is not { } target)
        {
            return Opaque(assignment, assignment.Target.Kind.ToString());
        }

        IrVar value = Value(assignment.Value);
        ssa.Store(current, target, value);
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

    /// <summary>An opaque call (receiver first, then arguments in parameter order) that may throw System.Exception.</summary>
    private IrVar? Invoke(IInvocationOperation invocation)
    {
        if (invocation.Instance is { } receiver && !receiver.Type!.IsValueType)
        {
            return Opaque(invocation, "dereference");
        }

        if (invocation.Arguments.Any(static a => a.Parameter!.RefKind is RefKind.Ref or RefKind.Out))
        {
            return Opaque(invocation, "ref-argument");
        }

        List<IrVar> args = invocation.Instance is { } instance ? [Value(instance)] : [];
        args.AddRange([.. invocation.Arguments
            .Select(a => (a.Parameter!.Ordinal, Value: Value(a.Value)))
            .ToList()
            .OrderBy(static a => a.Ordinal)
            .Select(static a => a.Value)]);
        IrVar? target = invocation.TargetMethod.ReturnsVoid ? null : ssa.Temp(TypeMapper.Map(invocation.Type!));
        IrVar threw = ssa.Temp(Bool);
        ssa.Emit(current, new IrCall(target, threw, CallIdentityFactory.Of(invocation.TargetMethod, renames), [.. args]));
        ThrowIf(threw, "System.Exception");
        return target;
    }
}
