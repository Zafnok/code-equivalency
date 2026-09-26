using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

namespace Equiv.Core.Execution;

/// <summary>
/// Turns a Divergent's model into drivers that call the legacy method on .NET Framework 4.8 and the modern method on
/// .NET 10 (ADR 0035 decision 2; ticket M4-009). Implemented by a language frontend, which alone holds the loaded projects.
/// </summary>
public interface IReplayDriverFactory
{
    /// <summary>
    /// Emits both sides' projects and the two drivers of <paramref name="pair"/> into <paramref name="directory"/>, with the
    /// case <paramref name="counterexample"/>'s inputs give each side. Never throws for an input it cannot build: the plan
    /// is then not constructible, with the reason.
    /// </summary>
    ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory);
}
