using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core.Matching;

/// <summary>
/// A project a frontend skipped (ADR 0029 decision 1), as plain data so Core never sees the loader.
/// <paramref name="Diagnostics"/> are display strings that carry the diagnostic ids (<c>CS0246: ...</c>).
/// <paramref name="Procedures"/> are the identities left unverified because of the skip: the project's own
/// procedures, when they could be enumerated, and the other side's procedures whose counterpart this project held.
/// A non-C# project (<paramref name="IsCSharp"/> false) is reported, but on its own it does not make the run incomplete.
/// </summary>
public sealed record UnverifiedProject(
    string Name,
    string AssemblyName,
    bool IsCSharp,
    ImmutableArray<string> Diagnostics,
    ImmutableArray<ProcedureIdentity> Procedures)
{
    // Deliberate non-short-circuit '&': see the comment on Equiv.Core.Configuration.EquivConfig.Equals.
    public bool Equals(UnverifiedProject? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
            & string.Equals(AssemblyName, other.AssemblyName, StringComparison.Ordinal) // NOSONAR
            & (IsCSharp == other.IsCSharp) // NOSONAR
            & IrEquality.SequenceEqual(Diagnostics, other.Diagnostics) // NOSONAR
            & IrEquality.SequenceEqual(Procedures, other.Procedures); // NOSONAR

    public override int GetHashCode() =>
        HashCode.Combine(Name, AssemblyName, IsCSharp, IrEquality.Hash(Diagnostics), IrEquality.Hash(Procedures));
}
