using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The <c>foreach</c> loops over a single-dimensional array in one graph, which lower to the index loop the compiler emits
/// instead of the enumerator calls the CFG desugars them into (ticket P1-004). A loop is found from the operation tree
/// and the CFG together: the tree's <see cref="IForEachLoopOperation"/> gives an array collection and a loop variable;
/// the CFG's assignment of <c>IEnumerator.Current</c> to that variable gives the enumerator's flow capture, and with it
/// the capture's <c>GetEnumerator</c>, the <c>MoveNext</c> that is the loop's condition and the <c>finally</c> that disposes
/// it. For an array Roslyn always desugars into exactly that shape, so those are read by cast, not tested. The element
/// read is the outermost operation of the element type over <c>Current</c>: the CFG's unboxing or downcast from
/// <c>object</c>, or <c>Current</c> itself for an <c>object[]</c>; any conversion on to the loop variable's type is lowered as
/// it stands. A loop whose variable is a deconstruction, or whose collection is anything else, is not found and lowers as
/// the CFG has it (ticket M4-001).
/// </summary>
internal sealed class ArrayForEachLoops
{
    private readonly Dictionary<IOperation, Site> sites = [];

    private readonly HashSet<ControlFlowRegion> finallys = [];

    private readonly Func<IOperation, (IOperation Array, IArrayTypeSymbol Type)?> known;

    private ArrayForEachLoops(Func<IOperation, (IOperation Array, IArrayTypeSymbol Type)?> known)
    {
        this.known = known;
    }

    /// <summary>What a recognised operation is in its loop.</summary>
    internal enum Role
    {
        /// <summary>The <c>GetEnumerator</c> capture: the array is evaluated and the index starts at 0.</summary>
        Start,

        /// <summary>The <c>MoveNext</c> call: the index is below the array's length.</summary>
        Condition,

        /// <summary>The <c>Current</c> read, converted to the element type: the element at the index, which then steps.</summary>
        Element,
    }

    /// <summary>
    /// The loops of <paramref name="cfg"/>. <paramref name="known"/> says of a collection that is not typed as an array
    /// which array it is known to be, and the operation that yields it, or null (ticket P2-117).
    /// </summary>
    public static ArrayForEachLoops Find(ControlFlowGraph cfg, Func<IOperation, (IOperation Array, IArrayTypeSymbol Type)?> known)
    {
        ArrayForEachLoops loops = new(known);
        Dictionary<ILocalSymbol, IArrayTypeSymbol> arrays = new(
            cfg.OriginalOperation.Descendants()
                .OfType<IForEachLoopOperation>()
                .Select(l => (Loop: l, loops.Enumerated(l.Collection)?.Type))
                .Where(static l => l.Type is not null && l.Loop.LoopControlVariable is IVariableDeclaratorOperation)
                .Select(static l => KeyValuePair.Create(((IVariableDeclaratorOperation)l.Loop.LoopControlVariable).Symbol, l.Type!)),
            SymbolEqualityComparer.Default);
        foreach (ISimpleAssignmentOperation assignment in cfg.Blocks.SelectMany(static b => b.Operations).OfType<ISimpleAssignmentOperation>())
        {
            if (assignment.Target is ILocalReferenceOperation { Local: var local } && arrays.TryGetValue(local, out IArrayTypeSymbol? type))
            {
                loops.Add(cfg, assignment.Value, type);
            }
        }

        return loops;
    }

    /// <summary>The role <paramref name="operation"/> plays in a recognised loop, or null when it plays none.</summary>
    public Site? Of(IOperation operation) => sites.GetValueOrDefault(operation);

    /// <summary>Whether <paramref name="region"/> is the <c>finally</c> of a recognised loop, which the index loop does not have.</summary>
    public bool IsElided(ControlFlowRegion region) => finallys.Contains(region);

    private void Add(ControlFlowGraph cfg, IOperation value, IArrayTypeSymbol type)
    {
        ImmutableArray<IOperation> chain = [.. Chain(value)];
        IFlowCaptureReferenceOperation current = (IFlowCaptureReferenceOperation)((IPropertyReferenceOperation)chain[^1]).Instance!;
        CaptureId enumerator = current.Id;
        IFlowCaptureOperation start = cfg.Blocks.SelectMany(static b => b.Operations).OfType<IFlowCaptureOperation>().First(c => c.Id.Equals(enumerator));
        BasicBlock header = cfg.Blocks.First(b => b.BranchValue is IInvocationOperation { Instance: IFlowCaptureReferenceOperation receiver } && receiver.Id.Equals(enumerator));
        IOperation collection = Enumerated(((IInvocationOperation)start.Value).Instance!).GetValueOrDefault().Array;
        // The MoveNext block is the `try` of the enumerator's `try`/`finally`.
        Loop loop = new(enumerator, collection, type, header.EnclosingRegion.EnclosingRegion!.NestedRegions[^1]);

        // Nothing is recorded when no operation over `Current` has the element type.
        ImmutableArray<(IOperation Operation, Site Site)> recognised =
        [
            .. chain
                .Where(o => SymbolEqualityComparer.Default.Equals(o.Type, type.ElementType))
                .TakeLast(1)
                .SelectMany(element => (IEnumerable<(IOperation, Site)>)[
                    (start, new Site(Role.Start, loop, collection)),
                    (header.BranchValue!, new Site(Role.Condition, loop, ((IInvocationOperation)header.BranchValue!).Instance!)),
                    (element, new Site(Role.Element, loop, current)),
                ]),
        ];
        foreach ((IOperation operation, Site site) in recognised)
        {
            sites[operation] = site;
            finallys.Add(loop.Finally);
        }
    }

    /// <summary>
    /// The array a <c>foreach</c> enumerates and its type, given the collection as the loop has it, in the tree or as the
    /// receiver of the CFG's <c>GetEnumerator</c>: the operand of the collection's conversion to the enumerated type, when
    /// that is a single-dimensional array, or what <see cref="known"/> says of the collection. Null for any other.
    /// </summary>
    private (IOperation Array, IArrayTypeSymbol Type)? Enumerated(IOperation collection) =>
        Chain(collection).Skip(1).Take(1)
            .Select(operand => operand.Type is IArrayTypeSymbol { IsSZArray: true } array ? (operand, array) : known(operand))
            .FirstOrDefault();

    /// <summary><paramref name="value"/> and the operands of the conversions it wraps, outermost first.</summary>
    private static IEnumerable<IOperation> Chain(IOperation value)
    {
        for (IOperation? operation = value; operation is not null; operation = (operation as IConversionOperation)?.Operand)
        {
            yield return operation;
        }
    }

    /// <summary>
    /// One recognised loop: its enumerator's capture, which holds the array instead, the collection, the array type, and the
    /// <c>finally</c> that disposes the enumerator.
    /// </summary>
    internal sealed record Loop(CaptureId Enumerator, IOperation Collection, IArrayTypeSymbol Type, ControlFlowRegion Finally);

    /// <summary>
    /// A recognised operation, its loop, and the operand it reads: the collection for <see cref="Role.Start"/>, else the
    /// enumerator's capture reference, which is the array whose nullness is checked.
    /// </summary>
    internal sealed record Site(Role Role, Loop Loop, IOperation Operand);
}
