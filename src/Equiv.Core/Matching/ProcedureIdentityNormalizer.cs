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

        string qualifiedType = Rename(@namespace, type, renames);
        string arity = genericArity > 0 ? $"`{genericArity.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        string parameters = string.Join(",", parameterTypes);
        return new ProcedureIdentity($"{qualifiedType}::{member}{arity}({parameters})");
    }

    public static ProcedureIdentity Endpoint(string verb, string route)
    {
        ArgumentNullException.ThrowIfNull(verb);
        ArgumentNullException.ThrowIfNull(route);
        return new ProcedureIdentity($"{verb.ToUpperInvariant()} {route}");
    }

    private static string Rename(string @namespace, string type, RenameMap renames)
    {
        string qualified = @namespace.Length == 0 ? type : $"{@namespace}.{type}";
        if (renames.Types.TryGetValue(qualified, out string? renamedType))
        {
            return renamedType;
        }

        string renamedNamespace = renames.Namespaces.GetValueOrDefault(@namespace, @namespace);
        return renamedNamespace.Length == 0 ? type : $"{renamedNamespace}.{type}";
    }
}
