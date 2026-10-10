namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// One operand of a call as a <see cref="RuntimeChangePrecondition"/> reads it (ticket P2-073): the name of the
/// <paramref name="Parameter"/> it is passed to, or <see cref="Receiver"/> for the receiver of an instance call, whether
/// that parameter <paramref name="IsString"/>, and, when the operand is a compile-time constant, its
/// <paramref name="Value"/> (an enum's as its underlying integer).
/// </summary>
public sealed record CallArgument(string Parameter, bool IsString, bool IsConstant, object? Value)
{
    /// <summary>The <see cref="Parameter"/> of an instance call's receiver.</summary>
    public const string Receiver = "this";
}
