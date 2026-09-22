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
/// <c>catch</c> whose type the thrown type converts to implicitly wins; every <c>finally</c> passed on
/// the way runs first, in the order it is passed. An exception with no known type -- an opaque call's
/// <c>threw</c> flag -- matches any <c>catch</c>, so more than one candidate makes the route unknown.
/// </summary>
internal static class ExceptionRegions
{
    /// <summary>
    /// The route an exception of <paramref name="thrown"/> takes out of <paramref name="block"/>:
    /// the finallys to run, the catch region that handles it (null when it leaves the procedure), and
    /// whether an unknown type could have gone to more than one catch. A null <paramref name="thrown"/>
    /// means the type is unknown.
    /// </summary>
    public static (ImmutableArray<ControlFlowRegion> Finallys, ControlFlowRegion? Handler, bool Ambiguous) Route(
        CSharpCompilation compilation,
        BasicBlock block,
        ITypeSymbol? thrown)
    {
        ImmutableArray<ControlFlowRegion>.Builder finallys = ImmutableArray.CreateBuilder<ControlFlowRegion>();
        ControlFlowRegion? handler = null;
        int candidates = 0;
        for (ControlFlowRegion? region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
        {
            if (region.Kind != ControlFlowRegionKind.Try)
            {
                continue;
            }

            if (region.EnclosingRegion is { Kind: ControlFlowRegionKind.TryAndFinally } wrapper)
            {
                if (handler is null)
                {
                    finallys.Add(wrapper.NestedRegions.First(static n => n.Kind == ControlFlowRegionKind.Finally));
                }

                continue;
            }

            candidates += MatchCatches(compilation, region.EnclosingRegion!, thrown, ref handler);
        }

        return (finallys.ToImmutable(), handler, thrown is null && candidates > 1);
    }

    /// <summary>
    /// Counts <paramref name="wrapper"/>'s <c>catch</c> regions and, when <paramref name="handler"/> is
    /// still unset, assigns it the first whose type an exception of <paramref name="thrown"/> converts
    /// to implicitly (or the first at all, when <paramref name="thrown"/> is unknown).
    /// </summary>
    private static int MatchCatches(CSharpCompilation compilation, ControlFlowRegion wrapper, ITypeSymbol? thrown, ref ControlFlowRegion? handler)
    {
        int candidates = 0;
        foreach (ControlFlowRegion candidate in wrapper.NestedRegions.Where(static n => n.Kind == ControlFlowRegionKind.Catch))
        {
            candidates++;
            if (handler is null && (thrown is null || compilation.ClassifyConversion(thrown, candidate.ExceptionType!).IsImplicit))
            {
                handler = candidate;
            }
        }

        return candidates;
    }

    /// <summary>The innermost <c>finally</c> region a block sits in, or null when it is in none.</summary>
    public static ControlFlowRegion? EnclosingFinally(BasicBlock block)
    {
        for (ControlFlowRegion? region = block.EnclosingRegion; region is not null; region = region.EnclosingRegion)
        {
            if (region.Kind == ControlFlowRegionKind.Finally)
            {
                return region;
            }
        }

        return null;
    }

    /// <summary>
    /// A <c>catch</c> this ticket does not lower: one with a <c>when</c> filter (a
    /// <c>FilterAndHandler</c> region), or a bare <c>catch</c>, which Roslyn gives the type
    /// <c>object</c> because no source type was written.
    /// </summary>
    public static bool HasUnsupportedCatch(ControlFlowRegion region) =>
        region.Kind == ControlFlowRegionKind.FilterAndHandler
        || (region.Kind == ControlFlowRegionKind.Catch && region.ExceptionType!.SpecialType == SpecialType.System_Object)
        || region.NestedRegions.Any(HasUnsupportedCatch);
}
