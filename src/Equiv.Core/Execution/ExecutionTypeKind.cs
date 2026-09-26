namespace Equiv.Core.Execution;

/// <summary>
/// What the input generators can build for a parameter (ticket M3-032): <c>bool</c>, <c>char</c>, each integer width,
/// IEEE binary32 and binary64, <c>decimal</c>, <c>string</c>, an enum, and <c>null</c> for any other reference type.
/// Anything else is <see cref="Unsupported"/>.
/// </summary>
public enum ExecutionTypeKind
{
    Unsupported,
    Boolean,
    Character,
    SignedByte,
    UnsignedByte,
    Signed16,
    Unsigned16,
    Signed32,
    Unsigned32,
    Signed64,
    Unsigned64,
    Binary32,
    Binary64,
    DecimalNumber,
    Text,
    Enum,

    /// <summary>A reference type other than <c>string</c>: the only value built for it is <c>null</c>.</summary>
    NullOnly,
}
