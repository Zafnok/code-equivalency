using Equiv.Core;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp;

/// <summary>
/// One procedure found by <see cref="ProcedureEnumerator.Enumerate"/>. <see cref="Identity"/> is the
/// raw (unrenamed) identity for this symbol alone; <see cref="CSharpFrontend"/> re-derives the
/// rename-applied identity used for matching (ticket M2-002 acceptance criterion 3).
/// <see cref="Location"/> is <c>Symbol.Locations[0]</c>, the identifier token Added/Removed SARIF
/// results point at.
/// </summary>
internal sealed record EnumeratedProcedure(ProcedureIdentity Identity, IMethodSymbol Symbol, Location Location);
