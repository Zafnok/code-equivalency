using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Which <c>catch</c> clauses of a graph name an exception that is a value (ticket P2-140): those whose variable the body
/// only ever reads as <c>e.Message</c>, outside a <c>when</c> filter, a lambda and a local function. <c>Message</c> does
/// not depend on where the exception was thrown; a stack trace does, and anything else that is handed the object can
/// read one, so every other use leaves the clause's exception opaque with reason <c>CaughtException</c>. A variable that
/// is never read names a value too.
/// </summary>
internal static class CaughtExceptions
{
    /// <summary>The operation that reads the exception of each clause of <paramref name="cfg"/> that names a value, by clause.</summary>
    public static Dictionary<ControlFlowRegion, ICaughtExceptionOperation> Named(ControlFlowGraph cfg)
    {
        Dictionary<ControlFlowRegion, ICaughtExceptionOperation> named = [];
        ILookup<ISymbol, ILocalReferenceOperation> reads = cfg.OriginalOperation.Descendants()
            .OfType<ILocalReferenceOperation>()
            .ToLookup(static r => (ISymbol)r.Local, SymbolEqualityComparer.Default);
        foreach (BasicBlock block in cfg.Blocks)
        {
            foreach (ICaughtExceptionOperation caught in block.Operations.SelectMany(static o => o.DescendantsAndSelf()).OfType<ICaughtExceptionOperation>())
            {
                // C# reads a caught exception in one place: the assignment to the clause's variable that starts the clause.
                ILocalSymbol local = ((ILocalReferenceOperation)((ISimpleAssignmentOperation)caught.Parent!).Target).Local;
                if (reads[local].All(IsMessage))
                {
                    named[Clause(block)] = caught;
                }
            }
        }

        return named;
    }

    /// <summary>
    /// The clause <paramref name="block"/> reads its exception in: the <c>catch</c> it starts, or the filtered clause whose
    /// filter it starts.
    /// </summary>
    public static ControlFlowRegion Clause(BasicBlock block)
    {
        ControlFlowRegion region = block.EnclosingRegion;
        return region.Kind == ControlFlowRegionKind.Filter ? region.EnclosingRegion! : region;
    }

    /// <summary>Whether <paramref name="read"/> is the receiver of <c>.Message</c>, evaluated where the handler runs.</summary>
    private static bool IsMessage(ILocalReferenceOperation read)
    {
        if (read.Parent is not IPropertyReferenceOperation { Property.Name: nameof(Exception.Message) })
        {
            return false;
        }

        IOperation inner = read;
        for (IOperation? outer = read.Parent; outer is not null; inner = outer, outer = outer.Parent)
        {
            if (outer is IAnonymousFunctionOperation or ILocalFunctionOperation || (outer is ICatchClauseOperation clause && clause.Filter == inner))
            {
                return false;
            }
        }

        return true;
    }
}
