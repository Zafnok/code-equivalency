namespace Equiv.Core.Execution;

/// <summary>
/// What the input generators can build for a parameter (ticket M3-032): <c>bool</c>, <c>char</c>, each integer width,
/// IEEE binary32 and binary64, <c>decimal</c>, <c>string</c>, an enum, <c>null</c> for any other reference type, and
/// the <c>System.Drawing</c> value types as two or four integer or binary32 components (ticket P2-051). Anything else is
/// <see cref="Unsupported"/>.
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

    /// <summary><c>System.Drawing.Point</c> or <c>Size</c>: two <c>int</c> components.</summary>
    Signed32Pair,

    /// <summary><c>System.Drawing.Rectangle</c>: four <c>int</c> components.</summary>
    Signed32Quad,

    /// <summary><c>System.Drawing.PointF</c> or <c>SizeF</c>: two <c>float</c> components.</summary>
    Binary32Pair,

    /// <summary><c>System.Drawing.RectangleF</c>: four <c>float</c> components.</summary>
    Binary32Quad,
}
