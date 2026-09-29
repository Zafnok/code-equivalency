using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Ir;

using ICSharpCode.Decompiler.IL;

using Microsoft.CodeAnalysis;

using IlBlock = ICSharpCode.Decompiler.IL.Block;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// The IL lowering's exception regions (ticket P1-015; M4-008's lowering), through <see cref="ExceptionLowerer"/>'s throw
/// blocks, <c>finally</c> and filter copies and route composition, which the IOperation lowering's CFG regions go through
/// too. A <c>try</c> block and each handler are lowered in place; a <c>finally</c> or <c>fault</c> block is copied onto
/// each path that leaves its <c>try</c> (a <c>fault</c>'s only when an exception does), and a <c>when</c> filter onto each
/// place an exception it may take is raised. An exception raised by a statement goes, walking out of the regions around
/// it, to the first handler whose type it converts to implicitly, a filtered one tried first and declined to the next,
/// behind each <c>finally</c> it leaves on the way; one of no known type that more than one handler could take is
/// <c>call-throw-in-try</c>, as <see cref="ExceptionRegions"/> routes an IOperation's. <c>throw new T(...)</c> is the
/// constructor's call and then a raise of <c>T</c>.
/// </summary>
internal sealed partial class IlLowerer
{
    /// <summary>The scope each region was lowered in, where its handlers are and where a copy of its blocks looks outside itself.</summary>
    private readonly Dictionary<TryInstruction, Scope> homes = [];

    /// <summary>The blocks being lowered: the main pass's, or one copy's of a <c>finally</c>, <c>fault</c> or filter.</summary>
    private Scope scope = new(parent: null);

    /// <summary>A <c>try</c>: its block, its handlers or its <c>finally</c> run after it, and then what follows it.</summary>
    private void Region(TryInstruction region)
    {
        homes[region] = scope;
        ImmutableArray<TryCatchHandler> handlers = region is TryCatch clauses ? [.. clauses.Handlers] : [];
        foreach (TryCatchHandler handler in handlers)
        {
            scope.Handlers[handler] = ssa.NewBlock();
        }

        Container((BlockContainer)region.TryBlock);
        IrBlockId after = context.Current;
        if (region is TryFinally)
        {
            after = ssa.NewBlock();
            Jump(Copy(region, after));
        }

        foreach (TryCatchHandler handler in handlers)
        {
            context.Current = scope.Handlers[handler];
            Container((BlockContainer)handler.Body);
            Jump(after);
        }

        context.Current = after;
    }

    /// <summary><c>throw new T(...)</c>: the constructor's call, with its own <c>threw</c> edge, then where a <c>T</c> goes.</summary>
    private void Throw(Throw thrown)
    {
        NewObj creation = (NewObj)thrown.Argument;
        _ = Call(creation);
        INamedTypeSymbol type = symbols.Method(creation.Method)!.ContainingType;
        Jump(Raise(TypeMapper.MetadataName(type), type));
    }

    /// <summary>The <c>finally</c> regions a branch or leave from <paramref name="from"/> to <paramref name="container"/> leaves, innermost first.</summary>
    private static ImmutableArray<ILInstruction> Crossed(ILInstruction from, ILInstruction container)
    {
        ImmutableArray<ILInstruction>.Builder crossed = ImmutableArray.CreateBuilder<ILInstruction>();
        for (ILInstruction child = from; child != container; child = child.Parent!)
        {
            if (child.Parent is TryFinally region && child == region.TryBlock)
            {
                crossed.Add(region);
            }
        }

        return crossed.ToImmutable();
    }

    /// <summary>Runs <paramref name="finallys"/>, innermost first, and then continues at <paramref name="destination"/>.</summary>
    private IrBlockId Unwind(IReadOnlyList<ILInstruction> finallys, IrBlockId destination) =>
        ExceptionLowerer.Unwind(finallys, destination, Copy);

    /// <summary>
    /// Where an exception of <paramref name="exceptionType"/>, of the type <paramref name="type"/> or of no known type when
    /// that is null, raised by the statement being lowered goes; in a filter, to the filter's false exit, since a filter that
    /// throws declines.
    /// </summary>
    private IrBlockId Raise(string exceptionType, ITypeSymbol? type)
    {
        if (scope.Filter is { } filter)
        {
            return filter.Declined;
        }

        List<ILInstruction> finallys = [];
        ImmutableArray<ExceptionLowerer.Clause<ILInstruction>>.Builder candidates = ImmutableArray.CreateBuilder<ExceptionLowerer.Clause<ILInstruction>>();
        bool taken = false;
        int clauses = 0;
        for (ILInstruction child = position; child.Parent is { } parent; child = parent)
        {
            if (parent is TryFinally or TryFault && child == ((TryInstruction)parent).TryBlock && !taken)
            {
                finallys.Add(parent);
            }

            if (parent is not TryCatch region || child != region.TryBlock)
            {
                continue;
            }

            foreach (TryCatchHandler handler in region.Handlers)
            {
                clauses++;
                if (!taken && (type is null || compilation.ClassifyCommonConversion(type, symbols.Type(handler.Variable.Type)!).IsImplicit))
                {
                    bool filtered = handler.Filter is BlockContainer;
                    candidates.Add(new ExceptionLowerer.Clause<ILInstruction>([.. finallys], scope.Handler(handler), filtered ? handler : null));
                    taken = !filtered;
                }
            }
        }

        return type is null && clauses > 1
            ? exceptions.Ambiguous(exceptionType)
            : exceptions.Raise(exceptionType, candidates.ToImmutable(), taken ? null : finallys, Copy, Filter);
    }

    /// <summary>A copy of a <c>finally</c> or <c>fault</c> block that runs and then continues at <paramref name="continuation"/>.</summary>
    private IrBlockId Copy(ILInstruction region, IrBlockId continuation) =>
        exceptions.Copy(region, continuation, () =>
        {
            IrBlockId entry = ssa.NewBlock();
            ILInstruction body = region is TryFinally finalizer ? finalizer.FinallyBlock : ((TryFault)region).FaultBlock;
            return (entry, () => Fill(new Scope(homes[(TryInstruction)region]), entry, (BlockContainer)body, continuation));
        });

    /// <summary>
    /// A copy of a handler's <c>when</c> filter that goes to <paramref name="taken"/> when it holds and to
    /// <paramref name="declined"/> when it does not or when it throws.
    /// </summary>
    private IrBlockId Filter(ILInstruction handler, IrBlockId taken, IrBlockId declined) =>
        exceptions.Filter(handler, taken, declined, () =>
        {
            IrBlockId entry = ssa.NewBlock();
            BlockContainer filter = (BlockContainer)((TryCatchHandler)handler).Filter;
            Scope home = homes[(TryInstruction)handler.Parent!];
            return (entry, () => Fill(new Scope(home) { Filter = new FilterExit(filter, taken, declined) }, entry, filter, exit: null));
        });

    /// <summary>Lowers <paramref name="body"/> in <paramref name="copy"/>, from <paramref name="entry"/>, and then goes back to where the lowering was.</summary>
    private void Fill(Scope copy, IrBlockId entry, BlockContainer body, IrBlockId? exit)
    {
        (Scope outer, IrBlockId current) = (scope, context.Current);
        scope = copy;
        context.Current = entry;
        Container(body, exit);
        (scope, context.Current) = (outer, current);
    }

    /// <summary>A filter copy's container, and where its verdict goes.</summary>
    private sealed record FilterExit(BlockContainer Container, IrBlockId Taken, IrBlockId Declined);

    /// <summary>
    /// The IR blocks of what one pass lowers: each container's blocks and exit and each handler's first block, and, for a
    /// handler it does not lower itself, the pass it was copied from.
    /// </summary>
    private sealed class Scope(Scope? parent)
    {
        public Dictionary<IlBlock, IrBlockId> Blocks { get; } = [];

        public Dictionary<BlockContainer, IrBlockId> Exits { get; } = [];

        public Dictionary<TryCatchHandler, IrBlockId> Handlers { get; } = [];

        /// <summary>In a filter's copy, the filter and where its verdict goes; null elsewhere.</summary>
        public FilterExit? Filter { get; init; }

        /// <summary>
        /// A handler's first block, which the pass that lowered its <c>try</c> made; a copy's own blocks and exits are all
        /// its own, since nothing leaves a <c>finally</c> or a filter but by its end.
        /// </summary>
        public IrBlockId Handler(TryCatchHandler handler) => Handlers.TryGetValue(handler, out IrBlockId? id) ? id : parent!.Handler(handler);
    }
}
