using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// Runs both drivers of a request (ADR 0035, ticket M3-032). Each side runs twice, each time in a fresh process, so
/// per-process nondeterminism such as string hash randomisation shows up as two different outcomes. A case that gets no
/// answer is <see cref="OutcomeKind.NotComparable"/>; its process is dropped and the next case starts a new one.
/// </summary>
internal sealed class DriverRunner(IDriverHost host, TimeSpan caseTimeout)
{
    public RunOutcomes Run(ExecutionDrivers drivers, ExecutionRequest request) => new(
        Side(drivers.Legacy, request),
        Side(drivers.Legacy, request),
        Side(drivers.Modern, request),
        Side(drivers.Modern, request));

    /// <summary>One run of <paramref name="driver"/> over <paramref name="request"/>'s cases, in one process while it answers.</summary>
    public List<ExecutionOutcome> Side(string driver, ExecutionRequest request)
    {
        using DriverStream stream = new(host, driver, caseTimeout);
        return [.. request.Inputs.SelectMany(input => request.Cultures.Select(culture => stream.Run(input, culture)))];
    }
}
