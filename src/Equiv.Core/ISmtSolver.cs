namespace Equiv.Core;

/// <summary>
/// A second SMT solver behind the verification backend (ADR 0050 decision 1; ticket P1-033). The backend prints a query
/// it gave up on as an SMT-LIB 2 script, which ends with <c>check-sat</c> and a <c>get-value</c> over the constants it
/// wants back, and reads the answer; it does not know which solver is behind this. <c>Equiv.Verify.Cvc5</c> implements
/// it by running the <c>cvc5</c> executable.
/// </summary>
public interface ISmtSolver
{
    /// <summary>The solver's name, as a result it answered is tagged with (<c>proofMethod: bounded+cvc5</c>).</summary>
    string Name { get; }

    /// <summary>The solver's version, as it reports it.</summary>
    string Version { get; }

    /// <summary>Answers <paramref name="script"/>, giving up after <paramref name="limit"/> of wall-clock time.</summary>
    SmtAnswer Ask(string script, TimeSpan limit);
}
