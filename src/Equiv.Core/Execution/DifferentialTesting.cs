namespace Equiv.Core.Execution;

/// <summary>
/// What running an Unknown pair on generated inputs showed (ADR 0035 decision 3; ticket P1-008), SARIF
/// <c>properties.differentialTesting</c>. <see cref="Inputs"/> ran; <see cref="Species"/> distinct behaviours were seen,
/// <see cref="Singletons"/> of them once; <see cref="DiscoveryProbability"/> is the Good-Turing estimate
/// (Böhme, Liyanage and Wüstholz, FSE 2021) that the next input shows a species not seen yet, under the generators'
/// distribution. It is a likelihood, never a proof, and never makes a result Equivalent. When the pair cannot be run,
/// <see cref="NotConstructible"/> says why and nothing else is set.
/// </summary>
public sealed record DifferentialTesting(int Inputs, int Species, int Singletons, double DiscoveryProbability, TestingStop StoppedBy, string? NotConstructible)
{
    /// <summary>How a species is told apart: both outcome classes, whether the outcomes are equal, and the IR path prefix.</summary>
    public const string SpeciesDefinition = "outcome+equal+irPrefix";

    /// <summary>The input distribution the estimate is stated for.</summary>
    public const string Distribution = "equiv generators v1";

    public static DifferentialTesting Tested(int inputs, int species, int singletons, double discoveryProbability, TestingStop stoppedBy) =>
        new(inputs, species, singletons, discoveryProbability, stoppedBy, NotConstructible: null);

    public static DifferentialTesting Unconstructible(string reason) => new(0, 0, 0, 0, TestingStop.Budget, reason);
}
