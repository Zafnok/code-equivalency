using System.Collections.Immutable;
using System.Linq;

using Equiv.Core;
using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// <c>try</c>/<c>catch</c>/<c>finally</c> (ticket M2-004 acceptance criterion 4): where an exception
/// raised in the block being lowered goes, duplicating a <c>finally</c> region onto every path that
/// leaves it, and a <c>when</c> filter onto every place an exception it may take is raised (ticket M4-008). <see cref="IrLowerer"/> reaches this through one instance (ticket P1-003). Re-lowering a
/// <c>finally</c> copy's own blocks stays <see cref="IrLowerer"/>'s job, so <see cref="Copy"/> calls
/// back into it through the <c>fill</c> delegate given at construction rather than naming its type.
/// Each region being lowered -- the main pass, or one copy of a <c>finally</c> -- gets its own
/// <see cref="LoweringContext"/>, so a method reading it can never see state an unrelated call left
/// behind (the bug ticket M2-004 PR #30 fixed: <c>Raise</c> resolving a <c>catch</c> through a block
/// map a <c>finally</c> copy had swapped in).
/// </summary>
internal sealed class ExceptionLowerer(SsaBuilder ssa, CSharpCompilation compilation, ControlFlowGraph cfg, SwitchChains chains, SourceSpan bodySpan, Action<BasicBlock, LoweringContext> fill)
{
    private readonly Dictionary<string, IrBlockId> throwBlocks = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Region, IrBlockId Continuation), IrBlockId> copies = [];
    private readonly Dictionary<(int Region, IrBlockId Taken, IrBlockId Declined), IrBlockId> filters = [];
    private IrBlockId? neverReached;

    /// <summary>Runs <paramref name="finallys"/> in order and then continues at <paramref name="destination"/>.</summary>
    public IrBlockId Unwind(ImmutableArray<ControlFlowRegion> finallys, IrBlockId destination, LoweringContext context)
    {
        for (int i = finallys.Length - 1; i >= 0; i--)
        {
            destination = Copy(finallys[i], destination, context);
        }

        return destination;
    }

    /// <summary>
    /// The block a CFG branch jumps to, with every <c>finally</c> it leaves copied in front of it. A branch
    /// out of a <c>try</c> whose <c>finally</c> never completes (it always throws, or loops forever) still
    /// names the block after the <c>try</c>, but Roslyn marks that block unreachable, so it was never
    /// lowered (ticket P2-010). The <c>finally</c> copy never reaches its exit, so nothing jumps to its
    /// continuation, which is <see cref="NeverReached"/>.
    /// </summary>
    public IrBlockId Destination(ControlFlowBranch branch, LoweringContext context) =>
        Unwind(branch.FinallyRegions, context.BlockIds.TryGetValue(branch.Destination!.Ordinal, out IrBlockId? lowered) ? lowered : NeverReached(), context);

    /// <summary>
    /// Where an exception of <paramref name="type"/> raised in the block being lowered goes: the first clause that takes
    /// it, or the shared throw block for <paramref name="exceptionType"/>, behind the <c>finally</c> regions it leaves on the
    /// way. A <c>when</c> filter is tried where the exception is raised, before any of those finallys, as .NET's first pass
    /// does; when it is false the next clause is tried (ticket M4-008). Inside a filter every exception goes to the
    /// filter's false exit: a filter that throws counts as false.
    /// </summary>
    public IrBlockId Raise(string exceptionType, ITypeSymbol? type, LoweringContext context)
    {
        if (context.Declined is { } declined)
        {
            return declined;
        }

        ExceptionRegions.ExceptionRoute route = ExceptionRegions.Route(compilation, context.Source, type);
        if (route.Ambiguous)
        {
            IrBlockId unknown = ssa.NewBlock();
            ssa.Emit(unknown, new IrOpaque(Target: null, "call-throw-in-try", bodySpan));
            ssa.Terminate(unknown, new IrThrow(exceptionType, []));
            return unknown;
        }

        IrBlockId? next = route.Uncaught is { } uncaught ? Unwind(uncaught, ThrowBlock(exceptionType), context) : null;
        for (int i = route.Candidates.Length - 1; i >= 0; i--)
        {
            (ImmutableArray<ControlFlowRegion> finallys, ControlFlowRegion clause) = route.Candidates[i];
            ControlFlowRegion handler = ExceptionRegions.Handler(clause);
            IrBlockId taken = Unwind(finallys, Handler(handler, context), context);
            next = clause == handler ? taken : Filter(ExceptionRegions.Filter(clause), handler, taken, next!, context);
        }

        return next!;
    }

    /// <summary>
    /// A copy of a <c>when</c> filter that goes to <paramref name="taken"/> when it holds and to <paramref name="declined"/>
    /// when it does not or when it throws (ticket M4-008). One copy per pair of exits; like a <c>finally</c> copy its
    /// blocks live in their own block map, in which the handler's first block is <paramref name="taken"/>, and their own
    /// <see cref="LoweringContext"/>, whose structured-exception-handling exit, the filter's false edge, is
    /// <paramref name="declined"/>.
    /// </summary>
    private IrBlockId Filter(ControlFlowRegion filter, ControlFlowRegion handler, IrBlockId taken, IrBlockId declined, LoweringContext context)
    {
        if (filters.TryGetValue((filter.FirstBlockOrdinal, taken, declined), out IrBlockId? existing))
        {
            return existing;
        }

        (Dictionary<int, IrBlockId> map, ImmutableArray<BasicBlock> blocks) = Blocks(filter);
        map[handler.FirstBlockOrdinal] = taken;
        IrBlockId entry = map[filter.FirstBlockOrdinal];
        filters[(filter.FirstBlockOrdinal, taken, declined)] = entry;

        LoweringContext copy = new(map, context.MainBlocks, declined) { Declined = declined };
        foreach (BasicBlock block in blocks)
        {
            fill(block, copy);
        }

        return entry;
    }

    /// <summary>
    /// A copy of a <c>finally</c> region that runs and then continues at <paramref name="continuation"/>
    /// (acceptance criterion 4: the blocks are duplicated onto every exit path). One copy per
    /// continuation; the copy's blocks live in their own block map and their own
    /// <see cref="LoweringContext"/>, whose structured-exception-handling exit is the jump to
    /// <paramref name="continuation"/>.
    /// </summary>
    private IrBlockId Copy(ControlFlowRegion region, IrBlockId continuation, LoweringContext context)
    {
        if (copies.TryGetValue((region.FirstBlockOrdinal, continuation), out IrBlockId? existing))
        {
            return existing;
        }

        (Dictionary<int, IrBlockId> map, ImmutableArray<BasicBlock> blocks) = Blocks(region);
        IrBlockId entry = map[region.FirstBlockOrdinal];
        copies[(region.FirstBlockOrdinal, continuation)] = entry;

        LoweringContext copy = new(map, context.MainBlocks, continuation);
        foreach (BasicBlock block in blocks)
        {
            fill(block, copy);
        }

        return entry;
    }

    /// <summary>A copied region's own blocks, those not in a region nested in it that is copied too, each given a new IR block.</summary>
    private (Dictionary<int, IrBlockId> Map, ImmutableArray<BasicBlock> Blocks) Blocks(ControlFlowRegion region)
    {
        ImmutableArray<BasicBlock> blocks =
            [.. cfg.Blocks
                .Where(b => b.Ordinal >= region.FirstBlockOrdinal && b.Ordinal <= region.LastBlockOrdinal)
                .Where(b => b.IsReachable && !chains.IsAbsorbed(b.Ordinal) && ExceptionRegions.EnclosingCopied(b) == region)];
        return (blocks.ToDictionary(static b => b.Ordinal, _ => ssa.NewBlock()), blocks);
    }

    /// <summary>
    /// The one continuation for every branch whose destination was not lowered, so that such exits share
    /// one copy of each <c>finally</c>. No edge reaches it, so <see cref="SsaBuilder.Build"/> drops it
    /// without reading its terminator, and it gets none.
    /// </summary>
    private IrBlockId NeverReached() => neverReached ??= ssa.NewBlock();

    /// <summary>
    /// The block a <c>catch</c> was lowered into. A <c>finally</c> copy fills a block map of its own, and
    /// an exception raised inside a <c>finally</c> unwinds to a <c>catch</c> outside it, which only the
    /// main pass lowered; a <c>catch</c> nested inside the <c>finally</c> is in the copy's own map.
    /// </summary>
    private static IrBlockId Handler(ControlFlowRegion handler, LoweringContext context) =>
        context.BlockIds.TryGetValue(handler.FirstBlockOrdinal, out IrBlockId? lowered) ? lowered : context.MainBlocks[handler.FirstBlockOrdinal];

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
}
