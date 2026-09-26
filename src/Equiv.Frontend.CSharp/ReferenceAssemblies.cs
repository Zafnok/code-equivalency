using System.Linq;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// Whether a symbol's assembly is a reference assembly (<c>ReferenceAssemblyAttribute</c>, as the .NET Framework
/// targeting pack and the .NET reference packs are): it cannot run, and the runtime supplies the real one. Shared by
/// <c>ProjectEmitter</c> (which never copies one for replay, ticket M4-009) and <c>CallIdentityFactory</c> (which flags a
/// call to one as external, ticket M3-033).
/// </summary>
internal static class ReferenceAssemblies
{
    private const string ReferenceAssemblyAttribute = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";

    public static bool IsReferenceAssembly(IAssemblySymbol assembly) =>
        assembly.GetAttributes().Any(static a => string.Equals(a.AttributeClass!.ToDisplayString(), ReferenceAssemblyAttribute, StringComparison.Ordinal));
}
