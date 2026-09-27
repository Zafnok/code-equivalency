using System.Collections.Immutable;

namespace Equiv.Verify.Z3.Ladder;

/// <summary>
/// What rung 5 sends a proposer (ticket P1-002): the IR text of both procedures, whose loops are the fragments rung 4
/// cut at every header, the relations to define (one per pair of an old and a new cut point, with each argument's name
/// and SMT-LIB sort: the header variables of both sides, paired), and every candidate Z3 rejected so far with why.
/// </summary>
internal sealed record InvariantRequest(
    string OldIr,
    string NewIr,
    ImmutableArray<InvariantRequest.Relation> Relations,
    ImmutableArray<InvariantRequest.Rejection> Rejected)
{
    /// <summary>A relation to define: its SMT-LIB name and its arguments in order.</summary>
    internal sealed record Relation(string Name, ImmutableArray<Variable> Parameters);

    /// <summary>A relation's argument: its SMT-LIB name and sort.</summary>
    internal sealed record Variable(string Name, string Sort);

    /// <summary>A candidate Z3 did not admit, and the parse error or the failed obligation with its counterexample.</summary>
    internal sealed record Rejection(string Candidate, string Reason);
}
