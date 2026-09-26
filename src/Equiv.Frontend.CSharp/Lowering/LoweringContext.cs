using Equiv.Core.Ir;

using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The state that is specific to one region being lowered: the main pass, or one copy of a
/// <c>finally</c> or a <c>when</c> filter (ticket M2-004's <c>Copy</c>, now <see cref="ExceptionLowerer.Copy"/>). Each region
/// gets its own instance, passed explicitly to every method that reads it, instead of the four fields
/// <c>Copy</c> used to swap on entry and restore on exit -- the shape that let <c>Raise</c> resolve a
/// <c>catch</c> through the wrong block map while a <c>finally</c> copy was in flight (ticket M2-004
/// PR #30 review fix; ticket P1-003).
/// </summary>
internal sealed class LoweringContext(Dictionary<int, IrBlockId> blockIds, Dictionary<int, IrBlockId> mainBlocks, IrBlockId? handlerExit)
{
    /// <summary>This region's own CFG-ordinal-to-IR-block map: the main pass's blocks, or one <c>finally</c> copy's own.</summary>
    public Dictionary<int, IrBlockId> BlockIds { get; } = blockIds;

    /// <summary>The main pass's block map, for a <c>catch</c> a <c>finally</c> copy does not itself contain.</summary>
    public Dictionary<int, IrBlockId> MainBlocks { get; } = mainBlocks;

    /// <summary>
    /// Where a structured-exception-handling exit goes: null in the main pass, the continuation in a <c>finally</c> copy, the
    /// false exit in a <c>when</c> filter copy.
    /// </summary>
    public IrBlockId? HandlerExit { get; } = handlerExit;

    /// <summary>
    /// Where every exception raised in this region goes: null except in a copy of a <c>when</c> filter, where it is the
    /// filter's false exit, since a filter that throws counts as false (ticket M4-008).
    /// </summary>
    public IrBlockId? Declined { get; init; }

    /// <summary>
    /// Where this graph's exit goes: null for the graph that is the body, whose exit returns; the next graph's entry for a
    /// field or property initializer run ahead of a constructor (ticket M4-008).
    /// </summary>
    public IrBlockId? Continuation { get; init; }

    /// <summary>The CFG block currently being filled.</summary>
    public BasicBlock Source { get; set; } = null!;

    /// <summary>The IR block instructions are currently being emitted into.</summary>
    public IrBlockId Current { get; set; } = new(0);
}
