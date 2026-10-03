using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.ApiEquivalences;
using Equiv.Core.Configuration;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// The canonical serialisation of a bound body that ADR 0024 fingerprints: one line per operation, indented by depth, holding
/// its kind, its syntax kind, whether it is implicit, its type, its constant, the symbols it references and, where it has
/// one, its <c>checked</c> context. Symbols are spelled as matching spells them (the rename map, and on the legacy side the
/// API-equivalence type entries and the member entries whose adapter passes every argument through unchanged); locals,
/// labels, lambdas, local functions and their parameters are numbered by first occurrence, and the method's own parameters
/// by position (ADR 0021). A call to a forwarder is spelled as the call to its target that the lowering makes it (ADR 0047).
/// So trivia, comments, local names and parameter names cannot change the text, and a different
/// overload, operator, conversion, constant or <c>checked</c> context does. A file path or line number the compiler supplies
/// for a caller-information parameter is written as <c>caller=</c> and its kind, without its value (ADR 0046). The walk also decides whether the body is
/// runtime-sensitive: whether a runtime rule applies to it inside the pair's interval (ADR 0040 decision 2; ticket
/// P2-055). Operations whose meaning is not in their kind, type and symbols (<c>dynamic</c> and
/// <see cref="OperationKind.None"/>) carry their source tokens instead, which costs congruence on a rename there but never
/// equates two different operations.
/// </summary>
internal sealed class BoundSerialiser : OperationWalker
{
    private static readonly ImmutableHashSet<SpecialType> FloatingPoint = [SpecialType.System_Single, SpecialType.System_Double];

    private static readonly ImmutableHashSet<SpecialType> Integral =
    [
        SpecialType.System_SByte, SpecialType.System_Byte, SpecialType.System_Int16, SpecialType.System_UInt16, SpecialType.System_Int32,
        SpecialType.System_UInt32, SpecialType.System_Int64, SpecialType.System_UInt64, SpecialType.System_Char, SpecialType.System_IntPtr,
        SpecialType.System_UIntPtr,
    ];

    private readonly StringBuilder text = new();
    private readonly Dictionary<ISymbol, string> numbered = new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol method;
    private readonly Compilation compilation;
    private readonly RenameMap renames;
    private readonly ImmutableArray<string> suppressedRuntimeChanges;
    private readonly ImmutableDictionary<string, string> types;
    private readonly ImmutableDictionary<string, string> members;
    private readonly SideRuntime runtime;
    private readonly ImmutableHashSet<string> keptForwarders;
    private readonly string interpolation;
    private readonly Func<IFlowAnonymousFunctionOperation, IOperation>? lambdas;
    private int depth;

    private bool RuntimeSensitive { get; set; }

    private BoundSerialiser(IMethodSymbol method, Compilation compilation, Settings settings, Func<IFlowAnonymousFunctionOperation, IOperation>? lambdas = null)
    {
        this.method = method;
        this.compilation = compilation;
        this.lambdas = lambdas;
        renames = settings.Renames;
        suppressedRuntimeChanges = settings.SuppressedRuntimeChanges;
        runtime = settings.Runtime;
        keptForwarders = settings.KeptForwarders;
        types = settings.Equivalences.Where(static e => e.IsType).ToImmutableDictionary(static e => e.Legacy, static e => e.Modern, StringComparer.Ordinal);
        members = settings.Equivalences.Where(static e => !e.IsType && PassesArgumentsThrough(e)).ToImmutableDictionary(static e => e.Legacy, static e => e.Modern, StringComparer.Ordinal);
        interpolation = ((CSharpCompilation)compilation).LanguageVersion >= LanguageVersion.CSharp10
            && compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler") is not null
                ? "DefaultInterpolatedStringHandler"
                : "string.Format";
    }

    /// <summary>
    /// How a side's symbols are spelled and which runtime rules apply to it: its rename map, the
    /// <c>runtime-changes.json</c> members not to flag, its API equivalences (the legacy side's; the modern side has none),
    /// and the pair's runtime interval with this side's x87 flag.
    /// </summary>
    public sealed record Settings(RenameMap Renames, ImmutableArray<string> SuppressedRuntimeChanges, ImmutableArray<ApiEquivalence> Equivalences, SideRuntime Runtime)
    {
        /// <summary>The identities of the forwarders the lowering does not resolve (ADR 0047), which are spelled as themselves here too.</summary>
        public ImmutableHashSet<string> KeptForwarders { get; init; } = [];
    }

    /// <summary>
    /// The serialisation of <paramref name="method"/>'s <paramref name="operations"/> (its body, after a constructor's field and
    /// property initializers), or of an auto-accessor when there are none, and whether it is runtime-sensitive: it references
    /// a <c>runtime-changes.json</c> member whose row applies inside the pair's interval and is not in <paramref name="settings"/>'
    /// suppressed runtime changes, converts a floating-point value to an integer on a pair that crosses .NET 9, or, on a side
    /// whose floating point alone runs on x87, handles a floating-point value at all. A same-runtime pair's body never is.
    /// </summary>
    public static (string Text, bool RuntimeSensitive) Serialise(
        IMethodSymbol method,
        Compilation compilation,
        ImmutableArray<IOperation> operations,
        Settings settings)
    {
        BoundSerialiser serialiser = new(method, compilation, settings);
        serialiser.text
            .Append(method.MethodKind).Append(" static=").Append(method.IsStatic).Append(" async=").Append(method.IsAsync)
            .Append(" returns=").Append(method.ReturnsVoid ? "void" : serialiser.Type(method.ReturnType))
            .Append(" (").AppendJoin(", ", method.Parameters.Select(p => $"{p.RefKind} {serialiser.Type(p.Type)}")).Append(")\n");
        if (operations.IsEmpty)
        {
            serialiser.text.Append("AutoAccessor init=").Append(method.IsInitOnly).Append('\n');
        }

        foreach (IOperation operation in operations)
        {
            serialiser.Visit(operation);
        }

        return (serialiser.text.ToString(), serialiser.RuntimeSensitive);
    }

    /// <summary>
    /// The serialisation of one expression-level <paramref name="fragment"/> of <paramref name="method"/>'s control-flow graph
    /// (ADR 0024 decision 2; ticket M4-004), and whether it is runtime-sensitive, by the rules of <see cref="Serialise"/>, with
    /// two differences. The graph holds a lambda as an <see cref="IFlowAnonymousFunctionOperation"/>, which has no body, so
    /// each one is serialised as the lambda <paramref name="lambdas"/> gives, body and all. And the method's own parameters
    /// are numbered by first occurrence, as its locals are, since a fragment is called with the variables it reads in that
    /// order, not with the method's parameters.
    /// </summary>
    public static (string Text, bool RuntimeSensitive) SerialiseFragment(
        IMethodSymbol method,
        Compilation compilation,
        IOperation fragment,
        Func<IFlowAnonymousFunctionOperation, IOperation> lambdas,
        Settings settings)
    {
        BoundSerialiser serialiser = new(method, compilation, settings, lambdas);
        serialiser.text.Append("Fragment").Append('\n');
        serialiser.Visit(fragment);
        return (serialiser.text.ToString(), serialiser.RuntimeSensitive);
    }

    /// <summary>
    /// Writes <paramref name="operation"/>'s line and then its children's. It does not dispatch through the visitor, which skips
    /// an <see cref="OperationKind.None"/> operation and would drop exactly the ones whose meaning only their tokens hold.
    /// </summary>
    public override void Visit(IOperation? operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RuntimeHelpers.EnsureSufficientExecutionStack();
        operation = operation is IFlowAnonymousFunctionOperation flow ? lambdas!(flow) : operation;
        text.Append(' ', depth * 2).Append(operation.Kind)
            .Append(" syntax=").Append((SyntaxKind)operation.Syntax.RawKind)
            .Append(" implicit=").Append(operation.IsImplicit);
        Append("type", operation.Type is { } type ? Type(type) : null);
        CallerLocationKind? caller = CallerLocation.Of(operation);
        Append("const", caller is null && operation.ConstantValue.HasValue ? Constant(operation.ConstantValue.Value) : null);
        Append("caller", caller?.ToString());
        Append("symbols", string.Join(", ", Symbols(operation).OfType<ISymbol>().Select(Render)));
        Append("context", Context(operation));
        text.Append('\n');

        RuntimeSensitive |= (runtime.X87 && IsFloatingPoint(operation.Type))
            || (runtime.FloatToIntegerChanged && operation is IConversionOperation { Operand.Type: { } from } conversion && IsFloatingPoint(from) && IsIntegral(conversion.Type!));

        depth++;
        foreach (IOperation child in operation.ChildOperations)
        {
            Visit(child);
        }

        depth--;
    }

    private void Append(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            text.Append(' ').Append(name).Append('=').Append(value);
        }
    }

    /// <summary>
    /// The symbols an operation references that its kind, type and children do not already say. A call to a forwarder
    /// references the forwarder's target, as the lowering calls it (ADR 0047; ticket P2-068).
    /// </summary>
    private ImmutableArray<ISymbol?> Symbols(IOperation operation) => operation switch
    {
        IInvocationOperation o => [Called(o.TargetMethod)],
        IObjectCreationOperation o => [o.Constructor],
        IMemberReferenceOperation o => [o.Member],
        ILocalReferenceOperation o => [o.Local],
        IParameterReferenceOperation o => [o.Parameter],
        IVariableDeclaratorOperation o => [o.Symbol],
        IArgumentOperation o => [o.Parameter],
        IAnonymousFunctionOperation o => [o.Symbol],
        ILocalFunctionOperation o => [o.Symbol],
        ILabeledOperation o => [o.Label],
        IBranchOperation o => [o.Target],
        IBinaryOperation o => [o.OperatorMethod],
        IUnaryOperation o => [o.OperatorMethod],
        IConversionOperation o => [o.OperatorMethod],
        ICompoundAssignmentOperation o => [o.OperatorMethod],
        IIncrementOrDecrementOperation o => [o.OperatorMethod],
        ITypeOfOperation o => [o.TypeOperand],
        IIsTypeOperation o => [o.TypeOperand],
        ISizeOfOperation o => [o.TypeOperand],
        IDeclarationPatternOperation o => [o.NarrowedType, o.DeclaredSymbol],
        IRecursivePatternOperation o => [o.NarrowedType, o.DeconstructSymbol, o.DeclaredSymbol],
        IListPatternOperation o => [o.NarrowedType, o.LengthSymbol, o.IndexerSymbol, o.DeclaredSymbol],
        IPatternOperation o => [o.NarrowedType],
        ICatchClauseOperation o => [o.ExceptionType],
        IWithOperation o => [o.CloneMethod],
        IFieldInitializerOperation o => [.. o.InitializedFields],
        IPropertyInitializerOperation o => [.. o.InitializedProperties],
        _ => [],
    };

    /// <summary>The <c>checked</c> context of an operation that has one, and the facts about the rest that no child carries.</summary>
    private string? Context(IOperation operation) => operation switch
    {
        IBinaryOperation o => Checked(o.IsChecked),
        IUnaryOperation o => Checked(o.IsChecked),
        IConversionOperation o => Checked(o.IsChecked),
        ICompoundAssignmentOperation o => Checked(o.IsChecked),
        IIncrementOrDecrementOperation o => Checked(o.IsChecked),
        IRelationalPatternOperation o => o.OperatorKind.ToString(),
        IInterpolatedStringOperation { Parent: not IInterpolatedStringHandlerCreationOperation } => interpolation,
        { Kind: OperationKind.None or OperationKind.DynamicMemberReference or OperationKind.DynamicInvocation or OperationKind.DynamicIndexerAccess or OperationKind.DynamicObjectCreation } =>
            Quote(string.Join(' ', operation.Syntax.DescendantTokens().Select(static t => t.Text))),
        _ => null,
    };

    /// <summary>The method a call to <paramref name="callee"/> calls: a forwarder's target, unless the pair keeps the forwarder.</summary>
    private IMethodSymbol Called(IMethodSymbol callee) =>
        keptForwarders.Contains(CallIdentityFactory.Name(callee, renames)) ? callee : Forwarders.Resolve(callee, compilation)?.Target ?? callee;

    private static string Checked(bool isChecked) => isChecked ? "checked" : "unchecked";

    private string Render(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } m =>
            $"{Number(m, "F")}(async={m.IsAsync} {string.Join(", ", m.Parameters.Select(p => $"{p.RefKind} {Type(p.Type)}"))}) -> {(m.ReturnsVoid ? "void" : Type(m.ReturnType))}",
        IMethodSymbol m => Method(m),
        IPropertySymbol p => string.Join('|', ((IMethodSymbol?[])[p.GetMethod, p.SetMethod]).OfType<IMethodSymbol>().Select(Method)),
        ILocalSymbol l => $"{Number(l, "L")}:{l.RefKind} {Type(l.Type)}",
        ILabelSymbol => Number(symbol, "B"),
        IParameterSymbol p when SymbolEqualityComparer.Default.Equals(p.ContainingSymbol, method) =>
            lambdas is null ? $"P{p.Ordinal.ToString(CultureInfo.InvariantCulture)}" : Number(p, "P"),
        IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } } => Number(symbol, "A"),
        IParameterSymbol p => $"#{p.Ordinal.ToString(CultureInfo.InvariantCulture)}",
        ITypeSymbol t => Type(t),
        _ => Member(symbol),
    };

    private string Number(ISymbol symbol, string prefix)
    {
        if (!numbered.TryGetValue(symbol, out string? name))
        {
            name = prefix + numbered.Count.ToString(CultureInfo.InvariantCulture);
            numbered[symbol] = name;
        }

        return name;
    }

    /// <summary>A callee as the IR's <c>IrCall</c> names it, rewritten by a pass-through API-equivalence entry; flags the body when a table row applies to it.</summary>
    private string Method(IMethodSymbol callee)
    {
        CallIdentity identity = CallIdentityFactory.Of(callee, renames, suppressedRuntimeChanges, runtime.Interval);
        if (members.TryGetValue(identity.Value, out string? modern))
        {
            identity = CallIdentityFactory.Of(modern, suppressedRuntimeChanges, runtime.Interval);
        }

        RuntimeSensitive |= identity.RuntimeChanged;
        return identity.Value;
    }

    /// <summary>A field or event as <c>Type::Name</c>; flags the body when a runtime-changes row applies to it.</summary>
    private string Member(ISymbol member)
    {
        string identity = $"{Type(member.ContainingType)}::{member.Name}";
        RuntimeSensitive |= CallIdentityFactory.Of(identity, suppressedRuntimeChanges, runtime.Interval).RuntimeChanged;
        return identity;
    }

    private string Type(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Error } => type.ToDisplayString(),
        IArrayTypeSymbol a => $"{Type(a.ElementType)}[{new string(',', a.Rank - 1)}]",
        INamedTypeSymbol { IsGenericType: true } n => $"{Named(n)}<{string.Join(',', n.TypeArguments.Select(Type))}>",
        INamedTypeSymbol n => Named(n),
        ITypeParameterSymbol { DeclaringMethod: { } declaring } p when !SymbolEqualityComparer.Default.Equals(declaring, method) => Number(p, "T"),
        ITypeParameterSymbol p => $"{p.TypeParameterKind}{p.Ordinal.ToString(CultureInfo.InvariantCulture)}",
        _ => type.ToDisplayString(), // dynamic, pointers, function pointers
    };

    private string Named(INamedTypeSymbol type) =>
        types.TryGetValue(TypeMapper.MetadataName(type.OriginalDefinition), out string? modern) ? modern : RoslynIdentity.TypeName(type, renames);

    /// <summary>A constant's invariant text: strings JSON-quoted and chars as their code, so neither can break a line.</summary>
    private static string Constant(object? value) => value switch
    {
        null => "null",
        string s => Quote(s),
        char c => ((int)c).ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";

    private static bool IsFloatingPoint(ITypeSymbol? type) => type is not null && FloatingPoint.Contains(Underlying(type).SpecialType);

    private static bool IsIntegral(ITypeSymbol type) => Integral.Contains(Underlying(type).SpecialType);

    /// <summary>A nullable value type's underlying type, and an enum's underlying integral type.</summary>
    private static ITypeSymbol Underlying(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable => Underlying(nullable.TypeArguments[0]),
        INamedTypeSymbol named => named.EnumUnderlyingType ?? named,
        _ => type,
    };

    /// <summary>
    /// A member entry whose modern argument list is the legacy call's source arguments in order and unchanged: rewriting only
    /// the callee's name then says exactly what the lowering's rewrite says. Any other adapter changes the arguments, which a
    /// fingerprint of the tree as bound cannot express, so its calls keep their legacy name and fall through to the solver.
    /// </summary>
    private static bool PassesArgumentsThrough(ApiEquivalence entry) =>
        entry.Arguments.SequenceEqual(Enumerable.Range(0, entry.Arguments.Length).Select(static i => new ApiArgument(i)));
}
