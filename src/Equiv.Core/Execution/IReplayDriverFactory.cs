using System.Text.Json;

using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

namespace Equiv.Core.Execution;

/// <summary>
/// Turns a Divergent's model into drivers that call each side's method on its project's detected runtime (ADR 0035
/// decision 2, ADR 0040 decision 3; tickets M4-009, P2-056), and an Unknown pair into drivers to test on generated inputs (decision 3;
/// ticket P1-008). Implemented by a language frontend, which alone holds the loaded projects.
/// </summary>
public interface IReplayDriverFactory
{
    /// <summary>
    /// Emits both sides' projects and the two drivers of <paramref name="pair"/> into <paramref name="directory"/>, with the
    /// case <paramref name="counterexample"/>'s inputs give each side. Never throws for an input it cannot build: the plan
    /// is then not constructible, with the reason.
    /// </summary>
    ReplayPlan Create(ProcedurePair pair, Counterexample counterexample, string directory);

    /// <summary>
    /// Emits both sides' projects and the two drivers of <paramref name="pair"/> into <paramref name="directory"/>, for
    /// generated inputs over the parameters both sides take, seeded with <paramref name="candidate"/>'s inputs when it has
    /// one they can be built from. Never throws for a pair it cannot call: the plan is then not constructible, with the reason.
    /// </summary>
    TestingPlan Plan(ProcedurePair pair, Counterexample? candidate, string directory);

    /// <summary>
    /// Emits both sides' projects and the two drivers of <paramref name="pair"/> into <paramref name="directory"/>, and
    /// builds one case from <paramref name="arguments"/>: JSON values in the method's own parameter order, its receiver
    /// (if any) excluded (ADR 0035, ADR 0036; ticket M5-002's <c>probe</c>). Never throws for an argument it cannot build:
    /// the plan is then not constructible, naming the parameter.
    /// </summary>
    ReplayPlan Probe(ProcedurePair pair, IReadOnlyList<JsonElement> arguments, string directory);
}
