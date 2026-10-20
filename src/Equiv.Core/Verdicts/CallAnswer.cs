using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// The answer a solver's model gave one open call of a delegate's <c>Invoke</c> whose receiver is an input (ADR 0062
/// decision 3; ticket P2-249): the call's <see cref="Callee"/> and <see cref="Arguments"/> as the run made it, the delegate
/// first, and its <see cref="Result"/> (null for a <c>void</c> call) or that it <see cref="Threw"/>. <see cref="Writes"/>
/// says the answer also gave a heap map the call reads a new version, which no delegate a driver builds does. An open
/// call's answer is the modular assumption, one function on both sides (ADR 0019), so a replay builds the delegate as this
/// table.
/// </summary>
public sealed record CallAnswer(CallIdentity Callee, ImmutableArray<IrValue> Arguments, IrValue? Result, bool Threw, bool Writes)
{
    public bool Equals(CallAnswer? other) =>
        other is not null
        && (Callee == other.Callee) & IrEquality.SequenceEqual(Arguments, other.Arguments) & (Result == other.Result) & (Threw == other.Threw) & (Writes == other.Writes);

    public override int GetHashCode() => HashCode.Combine(Callee, IrEquality.Hash(Arguments), Result, Threw, Writes);
}
