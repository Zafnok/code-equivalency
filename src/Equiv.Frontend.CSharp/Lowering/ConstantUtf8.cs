using System.Collections.Immutable;
using System.Linq;
using System.Text.Unicode;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Whether a UTF-8 decoding call reads bytes the method shows are constants that form well-formed UTF-8 (ticket P2-216): the
/// UTF-8 rows' change is the replacement of ill-formed input, so such a call decodes the same on every runtime. The bytes are
/// a new array of constants, or a local variable declared with one and never assigned again, whose elements only earlier
/// statements of the blocks the call runs in set, each to a constant at a constant index, and that nothing else uses before
/// the call.
/// </summary>
internal static class ConstantUtf8
{
    /// <summary>The longest array read: a longer one is no array a method fills by hand, and is not built here.</summary>
    private const int MaxLength = 4096;

    /// <summary>
    /// Whether <paramref name="decoding"/>, a call to <c>Encoding</c>'s or <c>UTF8Encoding</c>'s <c>GetString</c>,
    /// <c>GetChars</c> or <c>GetCharCount</c>, decodes constant well-formed UTF-8: an overload that takes a byte array alone, or
    /// a byte array, an index and a count (the only overloads with three parameters), with a constant index and count. The
    /// members are instance methods, which no forwarder calls, so the call is to one of them.
    /// </summary>
    public static bool IsWellFormed(IInvocationOperation decoding)
    {
        ImmutableArray<IParameterSymbol> parameters = decoding.TargetMethod.Parameters;
        if (parameters.Length switch { 1 => parameters[0].Type is not IArrayTypeSymbol, 3 => false, _ => true })
        {
            return false;
        }

        IOperation bytes = Argument(decoding, 0);
        byte[]? content = bytes switch
        {
            IArrayCreationOperation creation => Created(creation),
            ILocalReferenceOperation local => Filled(local, decoding),
            _ => null,
        };
        return content is not null && Range(decoding, content) is { } read && Utf8.IsValid(read);
    }

    private static IOperation Argument(IInvocationOperation call, int ordinal) => call.Arguments.Single(a => a.Parameter!.Ordinal == ordinal).Value;

    /// <summary>The bytes of the call's range of <paramref name="content"/>: all of them, or its constant index and count; null when not known.</summary>
    private static byte[]? Range(IInvocationOperation decoding, byte[] content) => decoding.Arguments.Length == 1
        ? content
        : Slice(content, Argument(decoding, 1).ConstantValue.Value, Argument(decoding, 2).ConstantValue.Value);

    /// <summary>The <paramref name="count"/> bytes of <paramref name="content"/> from <paramref name="index"/>, when both are <c>int</c> constants in range; null otherwise.</summary>
    private static byte[]? Slice(byte[] content, object? index, object? count) =>
        index is int start && count is int length && (uint)start <= (uint)content.Length && (uint)length <= (uint)(content.Length - start)
            ? content[start..(start + length)]
            : null;

    /// <summary>
    /// The content of a new byte array of a constant length, each element a constant or zero; null when not known. It is passed
    /// as a byte array, so it has one dimension.
    /// </summary>
    private static byte[]? Created(IArrayCreationOperation creation)
    {
        if (creation.DimensionSizes[0].ConstantValue.Value is not int length || length > MaxLength)
        {
            return null;
        }

        if (creation.Initializer is null)
        {
            return new byte[length];
        }

        byte[] content = [.. creation.Initializer.ElementValues.Select(static e => e.ConstantValue.Value).OfType<byte>()];
        return content.Length == length ? content : null;
    }

    /// <summary>
    /// The content of <paramref name="local"/>'s array when <paramref name="call"/> reads it, or null when the method does not
    /// show it. Between the declaration and the call there is no loop, no label a jump could go back to, no lambda and no
    /// local function, so each statement before the call in a block it runs in has run once, in order, and none after it has.
    /// </summary>
    private static byte[]? Filled(ILocalReferenceOperation local, IInvocationOperation call)
    {
        IOperation root = LocalValues.Root(local);
        if (LocalValues.Initializer(local.Local, root) is not IArrayCreationOperation creation
            || Created(creation) is not { } content
            || Declaration(local.Local, root) is not { } declaration
            || Blocks(call, declaration) is not { IsEmpty: false } blocks)
        {
            return null;
        }

        IEnumerable<IOperation> uses = LocalValues.References(local.Local, root).Where(reference => reference != local);
        if (!uses.All(use => IsWrittenBefore(use, local.Local, blocks) || IsUsedAfter(use, blocks)))
        {
            return null;
        }

        foreach ((IBlockOperation block, int at) in blocks)
        {
            foreach (IOperation statement in block.Operations.Take(at))
            {
                if (Write(statement, local.Local) is ({ } index, { } value))
                {
                    if (index >= content.Length)
                    {
                        return null;
                    }

                    content[index] = value;
                }
            }
        }

        return content;
    }

    /// <summary>
    /// The statement in a block that declares <paramref name="local"/>, which has an initializer; null when the declaration is
    /// no statement of a block (a <c>for</c>, a <c>using</c> or a switch section's).
    /// </summary>
    private static IOperation? Declaration(ILocalSymbol local, IOperation root)
    {
        IVariableDeclaratorOperation declarator = root.DescendantsAndSelf().OfType<IVariableDeclaratorOperation>().First(d => SymbolEqualityComparer.Default.Equals(d.Symbol, local));
        IOperation group = declarator.Parent!.Parent!;
        return group.Parent is IBlockOperation ? group : null;
    }

    /// <summary>
    /// The blocks <paramref name="call"/> runs in, from the one that holds <paramref name="declaration"/> inwards, each with the
    /// position of the statement that holds the call; empty when a loop, a lambda, a local function or a labelled block lies
    /// between them. The call reads the local variable, so it is in the block that declares it.
    /// </summary>
    private static ImmutableArray<(IBlockOperation Block, int At)> Blocks(IOperation call, IOperation declaration)
    {
        List<(IBlockOperation Block, int At)> blocks = [];
        for (IOperation inner = call; ; inner = inner.Parent!)
        {
            IOperation outer = inner.Parent!;
            if (outer is ILoopOperation or IAnonymousFunctionOperation or ILocalFunctionOperation
                || (outer is IBlockOperation labelled && labelled.Operations.OfType<ILabeledOperation>().Any()))
            {
                return [];
            }

            if (outer is IBlockOperation block)
            {
                blocks.Insert(0, (block, block.Operations.IndexOf(inner)));
                if (block.Operations.Contains(declaration))
                {
                    return [.. blocks];
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="use"/> is the array of an element write, <c>a[i] = b;</c> with constants <c>i</c> and <c>b</c>,
    /// that is a statement before the call in one of <paramref name="blocks"/>.
    /// </summary>
    private static bool IsWrittenBefore(IOperation use, ILocalSymbol local, ImmutableArray<(IBlockOperation Block, int At)> blocks) =>
        blocks.Any(b => b.Block.Operations.Take(b.At).Any(statement => Write(statement, local) is ({ }, { }) && Written(statement) == use));

    /// <summary>
    /// Whether <paramref name="use"/> is in a statement after the call in one of <paramref name="blocks"/>, and not in a lambda or
    /// a local function, which could run before it.
    /// </summary>
    private static bool IsUsedAfter(IOperation use, ImmutableArray<(IBlockOperation Block, int At)> blocks) =>
        blocks.Any(b => b.Block.Operations.Skip(b.At + 1).Any(statement => Holds(statement, use)))
        && !Ancestors(use).Any(static o => o is IAnonymousFunctionOperation or ILocalFunctionOperation);

    private static bool Holds(IOperation statement, IOperation use) => Ancestors(use).Contains(statement);

    private static IEnumerable<IOperation> Ancestors(IOperation operation)
    {
        for (IOperation? parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            yield return parent;
        }
    }

    /// <summary>The statement <c>local[index] = value;</c> with constants: its index and value, or nulls.</summary>
    private static (int? Index, byte? Value) Write(IOperation statement, ILocalSymbol local) =>
        statement is IExpressionStatementOperation { Operation: ISimpleAssignmentOperation { Target: IArrayElementReferenceOperation { ArrayReference: ILocalReferenceOperation array, Indices: [var index] }, Value: var value } }
        && SymbolEqualityComparer.Default.Equals(array.Local, local)
        && index.ConstantValue.Value is int i and >= 0
        && value.ConstantValue.Value is byte b
            ? (i, b)
            : (null, null);

    /// <summary>The array reference of the element write <paramref name="statement"/>, which <see cref="Write"/> has matched.</summary>
    private static IOperation Written(IOperation statement) =>
        ((IArrayElementReferenceOperation)((ISimpleAssignmentOperation)((IExpressionStatementOperation)statement).Operation).Target).ArrayReference;
}
