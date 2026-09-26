namespace Equiv.Core.Verdicts;

/// <summary>
/// The theory rung 4 of the loop ladder encoded bitvectors in (ticket P1-001; VERIFICATION-MODEL.md section 5.1). SARIF
/// <c>properties.chcMode</c>.
/// </summary>
public enum ChcMode
{
    /// <summary>
    /// Every bitvector is the mathematical integer it denotes, signed, within its width's bounds. Used only once a Spacer
    /// query proved that no integer operation the encoding keeps exact can overflow, since only then does it describe the
    /// bitvector runs.
    /// </summary>
    Integers,

    /// <summary>Bitvectors stay bitvectors: exact, and much harder for Spacer.</summary>
    BitVectors,
}
