namespace Equiv.Verify.Z3;

/// <summary>How a <see cref="ChcEncoder"/> reads a bitvector (<see cref="IntModeTranslator"/>).</summary>
internal enum ChcArithmetic
{
    /// <summary>As a bitvector.</summary>
    BitVectors,

    /// <summary>As the integer its bits denote, read signed; an exact operation whose result is out of bounds overflows.</summary>
    Integers,

    /// <summary>As the integer its bits denote, read signed; an exact operation wraps its result around into bounds.</summary>
    WrappingIntegers,
}
