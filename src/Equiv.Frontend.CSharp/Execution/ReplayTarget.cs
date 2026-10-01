using Equiv.Core;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// A procedure's symbol, the compilation of the project that declares it, and that project's detected runtime, which its
/// replay driver runs on (tickets M4-009, P2-056); null when the project has none, such as an unhosted <c>netstandard</c> one.
/// </summary>
internal sealed record ReplayTarget(IMethodSymbol Method, Compilation Compilation, TargetRuntime? Runtime);
