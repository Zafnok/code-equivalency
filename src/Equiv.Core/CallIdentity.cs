namespace Equiv.Core;

/// <summary>
/// Identity of an opaque callee. Equal identities are the same uninterpreted function, unless
/// <see cref="RuntimeChanged"/> is set (ticket M2-006): a call to a BCL member whose behaviour
/// differs between .NET Framework 4.8 and .NET even when the call is textually identical, so the
/// backend (M3-001) must never assume the old- and new-side calls agree. <see cref="External"/> is
/// set (ticket M3-033) when the callee's target assembly is one of the framework reference
/// assemblies the project compiled against, as opposed to the solution's own code or a NuGet
/// package; the census uses it to list every BCL member a lowered body calls.
/// </summary>
public sealed record CallIdentity(string Value, bool RuntimeChanged = false, bool External = false);
