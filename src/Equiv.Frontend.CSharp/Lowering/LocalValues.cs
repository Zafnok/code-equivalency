using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// What a method writes to its local variables and parameters, read from the bound tree of the whole method (tickets P2-074
/// and P2-190): the rules that follow a value into a local variable ask it whether the variable only ever holds such values.
/// </summary>
internal static class LocalValues
{
    /// <summary>
    /// Whether <paramref name="value"/> is one <paramref name="isSource"/> holds of, or reads a local variable that is
    /// declared with such a value and that every later assignment gives one.
    /// </summary>
    public static bool HoldsOnly(IOperation value, Func<IOperation, bool> isSource) => HoldsOnly(value, isSource, []);

    private static bool HoldsOnly(IOperation value, Func<IOperation, bool> isSource, ImmutableHashSet<ISymbol> seen) =>
        isSource(value)
        || (value is ILocalReferenceOperation local
            && !seen.Contains(local.Local)
            && Initializer(local.Local, Root(value)) is { } initial
            && Writes(local.Local, Root(value)).Prepend(initial).All(written => written is not null && HoldsOnly(written, isSource, seen.Add(local.Local))));

    /// <summary>The value <paramref name="local"/>'s declaration under <paramref name="root"/> gives it; null when it has none.</summary>
    public static IOperation? Initializer(ILocalSymbol local, IOperation root) =>
        root.DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().FirstOrDefault(d => SymbolEqualityComparer.Default.Equals(d.Symbol, local))?.Initializer?.Value;

    /// <summary>
    /// What is written to <paramref name="symbol"/> under <paramref name="root"/> after its declaration: the value of each
    /// assignment, or null for a write whose value is not known (a compound assignment, a <c>ref</c> or <c>out</c>
    /// argument, a tuple it is part of, or a <c>ref</c> to it that a ref local or a ref conditional takes, ticket P2-201).
    /// </summary>
    public static ImmutableArray<IOperation?> Writes(ISymbol symbol, IOperation root) =>
        [.. References(symbol, root).Select(Write).Where(static w => w.Written).Select(static w => w.Value)];

    /// <summary>Every read or write of the local variable, parameter, field or property <paramref name="symbol"/> under <paramref name="root"/>.</summary>
    public static IEnumerable<IOperation> References(ISymbol symbol, IOperation root) =>
        root.DescendantsAndSelf().Where(o => SymbolEqualityComparer.Default.Equals(Symbol(o), symbol));

    private static (bool Written, IOperation? Value) Write(IOperation reference) => reference.Parent switch
    {
        ISimpleAssignmentOperation assignment when assignment.Target == reference => (true, assignment.Value),
        IAssignmentOperation assignment when assignment.Target == reference => (true, null),
        IArgumentOperation { Parameter.RefKind: not RefKind.None } or ITupleOperation => (true, null),
        IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } } => (true, null),
        ISimpleAssignmentOperation { IsRef: true } or IConditionalOperation { IsRef: true } => (true, null),
        _ => (false, null),
    };

    /// <summary>
    /// The local variable, parameter, field or property (ticket P2-201) <paramref name="operation"/> reads; null for any other
    /// operation.
    /// </summary>
    public static ISymbol? Symbol(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation field => field.Field,
        IPropertyReferenceOperation property => property.Property,
        _ => null,
    };

    /// <summary>The top of the bound tree <paramref name="operation"/> is in.</summary>
    public static IOperation Root(IOperation operation) => operation.Parent is { } parent ? Root(parent) : operation;
}
