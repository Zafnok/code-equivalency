namespace Equiv.Core.Verdicts;

/// <summary>
/// A predicate over a pair's shared inputs under which the solver proved the pair Equivalent (ADR 0048; ticket P1-022),
/// as SARIF's <c>properties.agreesWhen</c>: <see cref="Smt"/> is SMT-LIB over the product's inputs, and <see cref="Text"/>
/// the same in source spelling with the modern side's parameter names. It is a hypothesis a checker admitted (ADR 0036):
/// <see cref="ProposedBy"/> harvested it from the two bodies, and rung 1's product proved it (<see cref="Method"/>).
/// </summary>
public sealed record AgreesWhen(string Smt, string Text)
{
    /// <summary>The proposer's name, as <c>agreesWhen.proposedBy</c>.</summary>
    public const string ProposedBy = "harvested-predicates";

    /// <summary>The rung whose product proved the condition, as <c>agreesWhen.proofMethod</c>.</summary>
    public const ProofMethod Method = ProofMethod.Bounded;
}
