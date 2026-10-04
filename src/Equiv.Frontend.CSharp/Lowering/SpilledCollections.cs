using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The collection expressions of one graph that are built by a constructor and an <c>Add</c>, and whose elements the CFG
/// evaluates ahead of them (ticket P2-120). An element that needs more than one operation, such as <c>new T { P = x }</c>
/// or <c>a ?? b</c>, is evaluated into a flow capture, and so is every element before it, in statements that come before
/// the collection expression. The collection initializer it replaces makes the object first and adds each element as soon
/// as it is evaluated, which is also what the compiled collection expression does. So the object is made where the first
/// such element starts, and the elements before a later one are added where that one starts (<see cref="At"/>); the
/// collection expression itself adds the rest (<see cref="Resume"/>). An element's statements are found by their syntax:
/// each lies inside the element's own, and the first of them in the graph's order is where the element starts.
/// </summary>
internal sealed class SpilledCollections
{
    private readonly Dictionary<IOperation, ImmutableArray<Start>> starts = [];

    private readonly Dictionary<ICollectionExpressionOperation, int> resumes = [];

    private SpilledCollections()
    {
    }

    /// <summary>Finds the starts of the elements of every collection expression of <paramref name="cfg"/> that <paramref name="adds"/> accepts.</summary>
    public static SpilledCollections Find(ControlFlowGraph cfg, Func<ICollectionExpressionOperation, bool> adds)
    {
        SpilledCollections spilled = new();
        ImmutableArray<IOperation> statements = [.. cfg.Blocks.SelectMany(static b => b.Operations.Append(b.BranchValue)).OfType<IOperation>()];

        // An outer collection expression comes first, so it is made before one nested in its first element.
        foreach (ICollectionExpressionOperation collection in statements
            .SelectMany(static s => s.DescendantsAndSelf())
            .OfType<ICollectionExpressionOperation>()
            .Where(adds)
            .OrderBy(static c => c.Syntax.SpanStart))
        {
            spilled.Add(collection, statements);
        }

        return spilled;
    }

    /// <summary>The elements that start at the statement or branch value <paramref name="operation"/>, outermost collection first.</summary>
    public ImmutableArray<Start> At(IOperation operation) => starts.GetValueOrDefault(operation, []);

    /// <summary>
    /// The first element of <paramref name="collection"/> that is not added yet where the collection expression itself
    /// is, or null when nothing was evaluated ahead of it and it is not made yet either.
    /// </summary>
    public int? Resume(ICollectionExpressionOperation collection) => resumes.TryGetValue(collection, out int resume) ? resume : null;

    private void Add(ICollectionExpressionOperation collection, ImmutableArray<IOperation> statements)
    {
        SeparatedSyntaxList<CollectionElementSyntax> elements = ((CollectionExpressionSyntax)collection.Syntax).Elements;
        int added = 0;
        for (int i = 0; i < elements.Count; i++)
        {
            if (statements.FirstOrDefault(s => elements[i].Span.Contains(s.Syntax.Span)) is not { } first)
            {
                continue;
            }

            starts[first] = [.. At(first), new Start(collection, added, i, !resumes.ContainsKey(collection))];
            resumes[collection] = added = i;
        }
    }

    /// <summary>
    /// Where element <paramref name="Element"/> of <paramref name="Collection"/> starts: the elements from
    /// <paramref name="From"/> up to it are evaluated by then and are added there, after the object is made when
    /// <paramref name="Makes"/> says this is the first element found.
    /// </summary>
    internal sealed record Start(ICollectionExpressionOperation Collection, int From, int Element, bool Makes);
}
