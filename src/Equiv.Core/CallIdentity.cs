namespace Equiv.Core;

/// <summary>
/// Identity of an opaque callee. Equal identities are the same uninterpreted function, unless
/// <see cref="RuntimeChanged"/> is set (ticket M2-006): a call to a BCL member whose behaviour
/// differs between .NET Framework 4.8 and .NET even when the call is textually identical, so the
/// backend (M3-001) must never assume the old- and new-side calls agree.
/// </summary>
public sealed record CallIdentity(string Value, bool RuntimeChanged = false);
