namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// One operand of a call as a <see cref="RuntimeChangePrecondition"/> reads it (ticket P2-073): the name of the
/// <paramref name="Parameter"/> it is passed to, or <see cref="Receiver"/> for the receiver of an instance call, whether
/// that parameter <paramref name="IsString"/>, and, when the operand is a compile-time constant, its
/// <paramref name="Value"/> (an enum's as its underlying integer). <see cref="ValidPath"/> is set on an operand the
/// method that makes the call shows to be a path .NET Framework's validation accepts (ticket P2-074).
/// </summary>
public sealed record CallArgument(string Parameter, bool IsString, bool IsConstant, object? Value)
{
    /// <summary>The <see cref="Parameter"/> of an instance call's receiver.</summary>
    public const string Receiver = "this";

    /// <summary>
    /// True when the operand came from one of the table's valid-path sources, or is used where one of its validating
    /// guards has passed on it (<see cref="RuntimeChangeTable.YieldsValidPath"/>, <see cref="RuntimeChangeTable.ValidatesPath"/>).
    /// </summary>
    public bool ValidPath { get; init; }

    /// <summary>
    /// True when the operand is passed to a <c>char</c> or <c>int</c> parameter: a character to search for, or a position
    /// or a count (ticket P2-181).
    /// </summary>
    public bool IsCharOrIndex { get; init; }
}
