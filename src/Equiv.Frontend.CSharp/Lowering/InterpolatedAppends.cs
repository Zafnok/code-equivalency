using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The handler-bound <c>StringBuilder.Append</c> and <c>AppendLine</c> calls of one graph that append what the same call
/// with the joined string appends (ticket P2-164). Before .NET 6 an interpolated string passed to either is built first, by
/// <c>string.Format</c> or <c>string.Concat</c>, and <c>Append(string)</c> or <c>AppendLine(string)</c> takes it. From
/// .NET 6 the call binds <c>Append(ref StringBuilder.AppendInterpolatedStringHandler)</c>: the graph makes the handler,
/// with the builder, into a flow capture, calls <c>AppendLiteral</c> or <c>AppendFormatted</c> on it once per part, each
/// writing into the builder, and then calls the member with it. A text part, a <c>string</c> hole, an integer hole and a
/// hole of a sealed class that is not <c>IFormattable</c> are appended as P2-086 and P2-139 join them, the integer with the
/// current culture, so the text appended is the same. What differs is when: the handler writes each part before the next
/// hole is evaluated.
/// <para>
/// So a call is taken only when the string is one <see cref="InterpolatedStrings.AreJoined"/> covers, no hole has a
/// format or alignment clause, the handler has no <c>IFormatProvider</c>, and no hole can tell the two orders apart. That
/// holds when every hole is a <c>string</c> or an integer read that runs no code and cannot throw, whatever the builder
/// is; or when the builder is one no code outside the body can reach (<see cref="IsUnreachable"/>), so a hole can neither
/// read it half written nor leave it half written for anyone to see once it throws out of the body.
/// </para>
/// </summary>
internal sealed class InterpolatedAppends
{
    private const string Handler = "System.Text.StringBuilder+AppendInterpolatedStringHandler";

    private readonly Dictionary<IInvocationOperation, Append> appends = [];

    private readonly HashSet<IOperation> absorbed = [];

    private InterpolatedAppends()
    {
    }

    /// <summary>Finds every call of <paramref name="cfg"/> that is the call of the same member with the joined string.</summary>
    public static InterpolatedAppends Find(ControlFlowGraph cfg, Compilation compilation)
    {
        InterpolatedAppends found = new();
        INamedTypeSymbol? handler = compilation.GetTypeByMetadataName(Handler);
        ImmutableArray<IOperation> statements = [.. cfg.Blocks.SelectMany(static b => b.Operations.Append(b.BranchValue)).OfType<IOperation>()];
        // The handler with no IFormatProvider takes the literal length, the hole count and the builder; the one with takes the provider too.
        foreach (IFlowCaptureOperation made in statements.OfType<IFlowCaptureOperation>().Where(c =>
            c.Value is IObjectCreationOperation creation && SymbolEqualityComparer.Default.Equals(creation.Type, handler) && creation.Arguments.Length == 3))
        {
            IInvocationOperation call = statements
                .SelectMany(static s => s.DescendantsAndSelf())
                .OfType<IInvocationOperation>()
                .First(c => c.Arguments.Any(a => Reads(a.Value, made.Id)));
            if (Joined(call, compilation) is { } member)
            {
                ImmutableArray<IInvocationOperation> parts = [.. statements.OfType<IInvocationOperation>().Where(p => Reads(p.Instance, made.Id))];
                found.appends[call] = new Append(member, made.Value, parts);
                found.absorbed.Add(made);
                found.absorbed.UnionWith(parts);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether <paramref name="statement"/> makes the handler of a taken call or appends a part to it: the call itself
    /// evaluates the parts, so the statement lowers to nothing.
    /// </summary>
    public bool IsAbsorbed(IOperation statement) => absorbed.Contains(statement);

    /// <summary>How <paramref name="call"/> is lowered when it is taken, or null.</summary>
    public Append? Of(IInvocationOperation call) => appends.GetValueOrDefault(call);

    private static bool Reads(IOperation? operation, CaptureId handler) => operation is IFlowCaptureReferenceOperation reference && reference.Id.Equals(handler);

    /// <summary>
    /// The member <paramref name="call"/> is the call of with the joined string, <c>Append(string)</c> or
    /// <c>AppendLine(string)</c>, when the two orders cannot be told apart; else null. It is decided on the call as it
    /// binds, whose parts carry their holes as written, before the graph moves any of them into a flow capture. Two
    /// interpolated strings joined by <c>+</c> make one handler too; that is left as it is.
    /// </summary>
    private static IMethodSymbol? Joined(IInvocationOperation call, Compilation compilation)
    {
        IInvocationOperation bound = (IInvocationOperation)compilation.GetSemanticModel(call.Syntax.SyntaxTree).GetOperation(call.Syntax)!;
        if (((IInterpolatedStringHandlerCreationOperation)bound.Arguments[0].Value).Content is not IInterpolatedStringOperation content)
        {
            return null;
        }

        IInvocationOperation[] formatted =
        [
            .. content.Parts
                .Cast<IInterpolatedStringAppendOperation>()
                .Where(static p => p.Kind == OperationKind.InterpolatedStringAppendFormatted)
                .Select(static p => (IInvocationOperation)p.AppendCall),
        ];
        IOperation[] holes = [.. formatted.Select(static f => f.Arguments[0].Value)];
        return formatted.All(static f => f.Arguments.Length == 1)
            && InterpolatedStrings.AreJoined(holes)
            && (holes.All(static h => InterpolatedStrings.IsRead(h) && (InterpolatedStrings.IsInteger(h) || h.Type!.SpecialType == SpecialType.System_String)) || IsUnreachable(bound, holes))
            ? call.TargetMethod.ContainingType.GetMembers(call.TargetMethod.Name).OfType<IMethodSymbol>().Single(static m => m.Parameters is [{ Type.SpecialType: SpecialType.System_String }])
            : null;
    }

    /// <summary>
    /// Whether no code but the body's own reaches the builder <paramref name="call"/> appends to, so that a hole neither
    /// reads it half written nor, by throwing, leaves it half written for anyone to see: the receiver is a local, or a
    /// chain of the builder's own calls on one, declared with <c>new StringBuilder(...)</c> or <c>new()</c> (a conversion of
    /// the creation with no operator method; the class is sealed, so the creation is of the builder); every use of the local
    /// is the receiver of a member of the builder whose result, when it is the builder again, is dropped or is such a
    /// receiver itself, and none is in a lambda or a local function; no hole names it; and the call is in no <c>try</c>
    /// statement, so no <c>catch</c> or <c>finally</c> of the body runs between a throwing hole and the end of the body.
    /// </summary>
    private static bool IsUnreachable(IInvocationOperation call, IOperation[] holes)
    {
        if (Builder(call.Instance!) is not { } local || Ancestors(call).Any(static a => a is ITryOperation))
        {
            return false;
        }

        IOperation body = Ancestors(call).Last();
        return body.Descendants().OfType<IVariableDeclaratorOperation>().Any(d =>
                SymbolEqualityComparer.Default.Equals(d.Symbol, local)
                && d.Initializer?.Value is IObjectCreationOperation or IConversionOperation { OperatorMethod: null, Operand: IObjectCreationOperation })
            && body.Descendants().OfType<ILocalReferenceOperation>().Where(r => SymbolEqualityComparer.Default.Equals(r.Local, local)).All(IsBuilderUse)
            && !holes.SelectMany(static h => h.DescendantsAndSelf()).OfType<ILocalReferenceOperation>().Any(r => SymbolEqualityComparer.Default.Equals(r.Local, local));
    }

    /// <summary>The local <paramref name="receiver"/> reads, directly or through a chain of the builder's own calls that return it; else null.</summary>
    private static ILocalSymbol? Builder(IOperation receiver) => receiver switch
    {
        ILocalReferenceOperation reference => reference.Local,
        IInvocationOperation { Instance: { } inner } when SymbolEqualityComparer.Default.Equals(inner.Type, receiver.Type) => Builder(inner),
        _ => null,
    };

    /// <summary>Whether <paramref name="use"/> of the builder only hands it to the builder's own members, outside every lambda and local function.</summary>
    private static bool IsBuilderUse(IOperation use) =>
        !Ancestors(use).Any(static a => a is IAnonymousFunctionOperation or ILocalFunctionOperation) && IsReceiver(use);

    /// <summary>
    /// Whether <paramref name="use"/> is the receiver of a call or property of the builder, whose result is not the builder,
    /// or is dropped, or is in turn such a receiver. An argument sits under an <see cref="IArgumentOperation"/>, so a call or
    /// property that is the parent is one <paramref name="use"/> is the receiver of.
    /// </summary>
    private static bool IsReceiver(IOperation use) => use.Parent switch
    {
        IInvocationOperation call => !SymbolEqualityComparer.Default.Equals(call.Type, use.Type) || call.Parent is IExpressionStatementOperation || IsReceiver(call),
        IPropertyReferenceOperation => true,
        _ => false,
    };

    private static IEnumerable<IOperation> Ancestors(IOperation operation)
    {
        for (IOperation? parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            yield return parent;
        }
    }

    /// <summary>
    /// A taken call: the <c>string</c> member it is lowered as a call of, the handler's creation, whose syntax is the
    /// interpolated string, and the <c>AppendLiteral</c> and <c>AppendFormatted</c> calls of its parts, in order.
    /// </summary>
    internal sealed record Append(IMethodSymbol Member, IOperation Handler, ImmutableArray<IInvocationOperation> Parts);
}
