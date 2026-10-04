using System.Collections.Immutable;

namespace Equiv.Core;

/// <summary>
/// Satisfiable. <see cref="Values"/> maps each constant the script asked the value of, by its name as the solver
/// printed it, to the value the solver printed. A solver may print a name with or without its <c>|...|</c>, whichever
/// the script used; both spell the same symbol.
/// </summary>
public sealed record SmtSat(ImmutableDictionary<string, string> Values) : SmtAnswer;
