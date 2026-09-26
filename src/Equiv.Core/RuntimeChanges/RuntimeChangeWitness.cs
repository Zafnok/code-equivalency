using System.Collections.Immutable;

namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// A measured row's evidence (ADR 0035, ticket M3-033): the generated input, the culture, and both runtimes' canonical
/// outcomes, exactly as <c>tools/runtime-diff</c> reported them. Each value is the report's own raw JSON text (never
/// re-parsed or reformatted), so a witness can be copied from a runtime-diff report straight into <c>runtime-changes.json</c>.
/// </summary>
public sealed record RuntimeChangeWitness(ImmutableArray<string> Input, string Culture, string Legacy, string Modern);
