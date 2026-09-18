using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Configuration;

namespace Equiv.Core.Matching;

/// <summary>
/// Builds the assembly-agnostic identity string VERIFICATION-MODEL.md section 4 defines: namespace,
/// type, member name and normalised parameter types for a member, or <c>VERB /route</c> for an
/// endpoint (ARCHITECTURE.md). Applies the config's namespace/type rename maps first, so a renamed
/// member on the legacy side normalises to the same <see cref="ProcedureIdentity"/> as its modern
/// counterpart.
/// </summary>
public static class ProcedureIdentityNormalizer
{
    public static ProcedureIdentity Member(string @namespace, string type, string member, int genericArity, ImmutableArray<string> parameterTypes, RenameMap renames)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(renames);

        string qualifiedType = Rename(Qualify(@namespace, type), renames);
        string arity = genericArity > 0 ? $"`{genericArity.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        string parameters = string.Join(",", parameterTypes.Select(p => Rename(p, renames)));
        return new ProcedureIdentity($"{qualifiedType}::{member}{arity}({parameters})");
    }

    public static ProcedureIdentity Endpoint(string verb, string route)
    {
        ArgumentNullException.ThrowIfNull(verb);
        ArgumentNullException.ThrowIfNull(route);
        return new ProcedureIdentity($"{verb.ToUpperInvariant()} {route}");
    }

    private static string Qualify(string @namespace, string type) => @namespace.Length == 0 ? type : $"{@namespace}.{type}";

    /// <summary>
    /// Renames a fully-qualified name (the member's own declaring type, or a parameter type):
    /// an exact match in <see cref="RenameMap.Types"/> wins outright; otherwise the namespace
    /// part (everything before the last '.') is looked up in <see cref="RenameMap.Namespaces"/>.
    /// A name with no '.' (a primitive like <c>int32</c>, or an unqualified name) has no
    /// namespace part and is returned unchanged. Nested generic syntax (<c>List&lt;Old.Ns.Order&gt;</c>)
    /// is not unwrapped; the frontend that builds <paramref name="parameterTypes"/> for <see cref="Member"/>
    /// is responsible for renaming inside such a name before calling this normaliser.
    /// </summary>
    private static string Rename(string qualifiedName, RenameMap renames)
    {
        if (renames.Types.TryGetValue(qualifiedName, out string? renamedType))
        {
            return renamedType;
        }

        int lastDot = qualifiedName.LastIndexOf('.');
        if (lastDot < 0)
        {
            return qualifiedName;
        }

        string @namespace = qualifiedName[..lastDot];
        string type = qualifiedName[(lastDot + 1)..];
        return Qualify(renames.Namespaces.GetValueOrDefault(@namespace, @namespace), type);
    }
}
