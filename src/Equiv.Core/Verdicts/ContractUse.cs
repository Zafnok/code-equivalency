namespace Equiv.Core.Verdicts;

/// <summary>
/// A callee contract an <see cref="Equivalent"/> caller rests on (ADR 0036 decision 2; ticket P1-010): the matched callee
/// pair's identity, the relational contract K the solver admitted for that pair, in SMT-LIB, and what proposed K. SARIF
/// <c>properties.contractsUsed</c>, one <c>{ callee, contract, proposedBy }</c> per entry.
/// </summary>
public sealed record ContractUse(string Callee, string Contract, string ProposedBy);
