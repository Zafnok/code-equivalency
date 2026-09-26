using System.Collections.Immutable;

namespace Equiv.Cli;

/// <summary>
/// Per side, the distinct <see cref="Equiv.Core.CallIdentity"/> values of calls into the runtime over every matched pair
/// (congruent ones included, as <see cref="RuntimeChangeCalls"/> counts), each with its call-site count, sorted by count
/// descending then ordinally (ADR 0035; ticket M3-033). <c>tools/corpus/corpus.ps1 -RuntimeDiff</c> takes the union of both
/// sides' top entries and runs <c>runtime-diff</c> on each.
/// </summary>
internal sealed record ExternalCallees(ImmutableArray<ExternalCallee> Legacy, ImmutableArray<ExternalCallee> Modern);
