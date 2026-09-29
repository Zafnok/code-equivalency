using System.Collections.Immutable;
using System.Linq;

using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;

using Microsoft.CodeAnalysis;

using IlType = ICSharpCode.Decompiler.TypeSystem.IType;
using SymbolKind = ICSharpCode.Decompiler.TypeSystem.SymbolKind;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// ILSpy's types and methods as the loaded compilation's own symbols (ADR 0039): a definition by its documentation ID
/// (<see cref="IdStringProvider.GetIdString(IEntity)"/> read back by <see cref="DocumentationCommentId"/>), a generic
/// instantiation rebuilt with <c>Construct</c> from its resolved type arguments, and a type parameter as the lowered
/// method's or its type's own. <see cref="TypeMapper"/> and <see cref="CallIdentityFactory"/> then run on the result
/// unchanged, so a call has one identity whichever lowering produced it. Null means the reference does not resolve: a
/// <c>ref</c> or function-pointer type, a type from a reference that did not load, or a member no symbol declares.
/// </summary>
internal sealed class IlSymbols(Compilation compilation, IMethodSymbol method)
{
    /// <summary>The type parameters of <paramref name="method"/>'s type, its outer types' first, as ILSpy numbers them.</summary>
    private readonly ImmutableArray<ITypeParameterSymbol> typeParameters = [.. Outermost(method.ContainingType).SelectMany(static t => t.TypeParameters)];

    private readonly Dictionary<IlType, ITypeSymbol?> types = [];

    private readonly Dictionary<IMethod, IMethodSymbol?> methods = [];

    public ITypeSymbol? Type(IlType type)
    {
        if (!types.TryGetValue(type, out ITypeSymbol? symbol))
        {
            symbol = Resolve(type);
            types[type] = symbol;
        }

        return symbol;
    }

    /// <summary>The method <paramref name="member"/> names, constructed as the call site constructs it.</summary>
    public IMethodSymbol? Method(IMethod member)
    {
        if (!methods.TryGetValue(member, out IMethodSymbol? symbol))
        {
            symbol = Resolve(member);
            methods[member] = symbol;
        }

        return symbol;
    }

    private ITypeSymbol? Resolve(IlType type) => type switch
    {
        ITypeParameter { OwnerType: SymbolKind.Method } parameter => method.TypeParameters[parameter.Index],
        ITypeParameter parameter => typeParameters[parameter.Index],
        ArrayType array => Type(array.ElementType) is { } element ? compilation.CreateArrayTypeSymbol(element, array.Dimensions) : null,
        ParameterizedType generic => Generic(generic),
        ITypeDefinition definition => Definition(definition),
        _ => null,
    };

    private IMethodSymbol? Resolve(IMethod member)
    {
        if (DocumentationCommentId.GetFirstSymbolForDeclarationId(IdStringProvider.GetIdString(member.MemberDefinition), compilation) is not IMethodSymbol definition
            || Type(member.DeclaringType) is not INamedTypeSymbol declaring
            || Resolved(member.TypeArguments) is not { } arguments)
        {
            return null;
        }

        IMethodSymbol constructed = declaring.GetMembers(definition.Name).OfType<IMethodSymbol>().First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definition));
        return arguments.IsEmpty ? constructed : constructed.Construct([.. arguments]);
    }

    private INamedTypeSymbol? Definition(ITypeDefinition definition) =>
        DocumentationCommentId.GetFirstSymbolForDeclarationId(IdStringProvider.GetIdString(definition), compilation) as INamedTypeSymbol;

    private INamedTypeSymbol? Generic(ParameterizedType generic) =>
        Definition(generic.GetDefinition()) is { } definition && Resolved(generic.TypeArguments) is { } arguments ? Construct(definition, arguments) : null;

    /// <summary>
    /// <paramref name="definition"/> constructed with <paramref name="arguments"/>, which ILSpy lists outer types' first: a
    /// nested type is found in its constructed outer type, then constructed with the rest.
    /// </summary>
    private static INamedTypeSymbol Construct(INamedTypeSymbol definition, ImmutableArray<ITypeSymbol> arguments)
    {
        int outer = arguments.Length - definition.Arity;
        INamedTypeSymbol type = outer == 0
            ? definition
            : Construct(definition.ContainingType, arguments[..outer]).GetTypeMembers(definition.Name, definition.Arity)[0];
        return definition.Arity == 0 ? type : type.Construct([.. arguments[outer..]]);
    }

    private ImmutableArray<ITypeSymbol>? Resolved(IReadOnlyList<IlType> types)
    {
        ImmutableArray<ITypeSymbol?> resolved = [.. types.Select(Type)];
        return resolved.Contains(null) ? null : [.. resolved.OfType<ITypeSymbol>()];
    }

    private static IEnumerable<INamedTypeSymbol> Outermost(INamedTypeSymbol type) =>
        type.ContainingType is { } outer ? Outermost(outer).Append(type) : [type];
}
