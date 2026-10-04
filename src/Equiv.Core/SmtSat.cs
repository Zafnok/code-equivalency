using System.Collections.Immutable;

namespace Equiv.Core;

/// <summary>
/// Satisfiable. <see cref="Values"/> maps each constant the script asked the value of, by the name the script
/// gave it (with its <c>|...|</c> when it has them), to the value the solver printed.
/// </summary>
public sealed record SmtSat(ImmutableDictionary<string, string> Values) : SmtAnswer;
