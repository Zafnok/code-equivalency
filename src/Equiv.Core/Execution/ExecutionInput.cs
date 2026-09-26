namespace Equiv.Core.Execution;

/// <summary>
/// One case's arguments, in parameter order with an instance member's receiver first. Each is JSON text in the driver's
/// wire form: an integer in decimal, a <c>char</c> as its code point, a <c>float</c> or <c>double</c> as a string holding
/// its IEEE bit pattern in hex (<c>"0x3FF0000000000000"</c>), a <c>decimal</c> as its four <c>GetBits</c> integers, an
/// enum as its underlying integer, and a string, a <c>bool</c> or <c>null</c> as themselves.
/// </summary>
public sealed record ExecutionInput(IReadOnlyList<string> Arguments);
