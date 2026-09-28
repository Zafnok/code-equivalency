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

    /// <summary>
    /// A candidate Z3 did not admit, and the parse error or the failed obligation with its counterexample. For a broken
    /// rule, <see cref="Premise"/> and <see cref="Conclusion"/> are its relations with the counterexample's values (null
    /// for the entry and for <c>bad</c>), so that a proposer can weaken the conclusion's definition (ticket P1-009).
    /// </summary>
    internal sealed record Rejection(string Candidate, string Reason)
    {
        public Fact? Premise { get; init; }

        public Fact? Conclusion { get; init; }
    }

    /// <summary>A relation applied to values: each argument's name and its value as Z3 prints it in SMT-LIB.</summary>
    internal sealed record Fact(string Relation, ImmutableArray<Binding> Values);

    /// <summary>An argument's name and value.</summary>
    internal sealed record Binding(string Name, string Value);
}
