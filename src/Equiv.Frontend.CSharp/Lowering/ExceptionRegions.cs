using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Where an exception raised in a block goes (ticket M2-004 acceptance criterion 4). Walking out from
/// the block, each enclosing <c>Try</c> region offers either its <c>catch</c> clauses, when its parent
/// is a <c>TryAndCatch</c>, or its <c>finally</c>, when its parent is a <c>TryAndFinally</c>. The first
/// <c>catch</c> whose type the thrown type converts to implicitly wins; a bare <c>catch</c>, which Roslyn gives the type
/// <c>object</c>, is one every thrown type converts to, in its place in source order (ticket M4-008). A <c>catch</c> with a
/// <c>when</c> filter is a candidate that may decline, so the walk goes on past it. Every <c>finally</c> passed on the way to
/// a handler runs first, in the order it is passed. An exception with no known type -- an opaque call's
/// <c>threw</c> flag -- matches any <c>catch</c>, so more than one candidate makes the route unknown.
/// </summary>
internal static class ExceptionRegions
{
    /// <summary>
    /// The route an exception of <paramref name="thrown"/> takes out of <paramref name="block"/>: the clauses that may
    /// take it, in the order they are tried, each with the finallys that run before its handler; the finallys that run
    /// when none takes it (null when an unfiltered <c>catch</c> always does); and whether an unknown type could have gone
    /// to more than one catch. A null <paramref name="thrown"/> means the type is unknown.
    /// </summary>
    public static ExceptionRoute Route(CSharpCompilation compilation, BasicBlock block, ITypeSymbol? thrown)
    {
        ImmutableArray<ControlFlowRegion>.Builder finallys = ImmutableArray.CreateBuilder<ControlFlowRegion>();
        ImmutableArray<Candidate>.Builder candidates = ImmutableArray.CreateBuilder<Candidate>();
        bool caught = false;
        int clauses = 0;
        for (ControlFlowRegion? region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
        {
            if (region.Kind != ControlFlowRegionKind.Try)
            {
                continue;
            }

            ControlFlowRegion wrapper = region.EnclosingRegion!;
            if (wrapper.Kind == ControlFlowRegionKind.TryAndFinally)
            {
                if (!caught)
                {
                    finallys.Add(wrapper.NestedRegions.First(static n => n.Kind == ControlFlowRegionKind.Finally));
                }

                continue;
            }

            foreach (ControlFlowRegion clause in wrapper.NestedRegions.Where(static n => n.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler))
            {
                clauses++;
                if (!caught && (thrown is null || compilation.ClassifyConversion(thrown, clause.ExceptionType!).IsImplicit))
                {
                    candidates.Add(new Candidate(finallys.ToImmutable(), clause));
                    caught = clause.Kind == ControlFlowRegionKind.Catch;
                }
            }
        }

        return new ExceptionRoute(candidates.ToImmutable(), caught ? null : finallys.ToImmutable(), thrown is null && clauses > 1);
    }

    /// <summary>
    /// The innermost region a block sits in that is never lowered in place but copied: a <c>finally</c>, onto each path
    /// that leaves its <c>try</c>, or a <c>when</c> filter, onto each place an exception it may take is raised (ticket
    /// M4-008). Null when it is in neither.
    /// </summary>
    public static ControlFlowRegion? EnclosingCopied(BasicBlock block)
    {
        for (ControlFlowRegion? region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
        {
            if (region.Kind is ControlFlowRegionKind.Finally or ControlFlowRegionKind.Filter)
            {
                return region;
            }
        }

        return null;
    }

    /// <summary>A clause's handler: the clause itself for a <c>catch</c>, the <c>catch</c> inside it for a filtered one.</summary>
    public static ControlFlowRegion Handler(ControlFlowRegion clause) =>
        clause.Kind == ControlFlowRegionKind.Catch ? clause : clause.NestedRegions.First(static n => n.Kind == ControlFlowRegionKind.Catch);

    /// <summary>A filtered clause's <c>when</c> filter.</summary>
    public static ControlFlowRegion Filter(ControlFlowRegion clause) => clause.NestedRegions.First(static n => n.Kind == ControlFlowRegionKind.Filter);

    /// <summary>A clause that may take the exception, and the finallys that run before its handler does.</summary>
    internal sealed record Candidate(ImmutableArray<ControlFlowRegion> Finallys, ControlFlowRegion Clause);

    /// <summary>
    /// The clauses tried in order, and the finallys run when every one declines, or null when an unfiltered <c>catch</c>
    /// among them always takes it.
    /// </summary>
    internal sealed record ExceptionRoute(ImmutableArray<Candidate> Candidates, ImmutableArray<ControlFlowRegion>? Uncaught, bool Ambiguous);
}
