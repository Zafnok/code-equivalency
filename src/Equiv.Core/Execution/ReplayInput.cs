namespace Equiv.Core.Execution;

/// <summary>
/// How a driver built one input (ADR 0062 decision 5; ticket P2-245): <see cref="Name"/> is <c>this</c> or the parameter's
/// name, and <see cref="Built"/> one of <c>value</c> (a scalar, a string, an array of values, a token, or a struct built from
/// its fields' values), <c>constructor &lt;signature&gt;</c>, <c>uninitialized &lt;fields&gt;</c> (an instance created
/// without a constructor, with the fields its graph was given, <c>Type.field</c>, comma-separated) or <c>delegate</c>.
/// </summary>
public sealed record ReplayInput(string Name, string Built);
