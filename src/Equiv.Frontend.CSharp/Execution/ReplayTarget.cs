using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>A procedure's symbol and the compilation of the project that declares it, for replay (ticket M4-009).</summary>
internal sealed record ReplayTarget(IMethodSymbol Method, Compilation Compilation);
