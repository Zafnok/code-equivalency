using System.Collections.Immutable;

namespace Equiv.Frontend.CSharp.Fingerprinting;

/// <summary>
/// A body's <see cref="BoundSerialiser"/> text as a pair's difference reads it: its <see cref="Lines"/> (ticket P2-173) and
/// the runtime rules that apply to it (ticket P2-181), each once, in the order the text first meets it.
/// </summary>
internal sealed record BoundText(ImmutableArray<BoundLine> Lines, ImmutableArray<string> Rules);
