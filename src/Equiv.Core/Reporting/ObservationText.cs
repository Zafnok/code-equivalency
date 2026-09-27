using Equiv.Core.Execution;

namespace Equiv.Core.Reporting;

/// <summary>
/// A deterministic text rendering of an <see cref="ObservedDivergence"/> (ticket P1-008): <see cref="Model"/> is the input
/// and its culture, SARIF <c>properties.model</c>; <see cref="Dump"/> adds both runtimes' canonical outcomes, for the
/// message and the result fingerprint.
/// </summary>
internal static class ObservationText
{
    public static string Model(ObservedDivergence observed) =>
        $"inputs({string.Join(", ", observed.Legacy.Input.Arguments)}) culture({observed.Legacy.Culture})";

    public static string Dump(ObservedDivergence observed) =>
        $"{Model(observed)} legacy({Outcome(observed.Legacy)}) modern({Outcome(observed.Modern)})";

    private static string Outcome(ExecutionOutcome outcome) => $"{(outcome.Kind == OutcomeKind.Threw ? "threw" : "returned")} {outcome.Canonical}";
}
