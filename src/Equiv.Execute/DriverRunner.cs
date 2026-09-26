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

    private List<ExecutionOutcome> Side(string driver, ExecutionRequest request)
    {
        List<ExecutionOutcome> outcomes = [];
        IDriverSession? session = null;
        try
        {
            foreach (ExecutionInput input in request.Inputs)
            {
                foreach (string culture in request.Cultures)
                {
                    session ??= host.Start(driver);
                    if (session.Exchange(OutcomeLine.Case(culture, input), caseTimeout) is { } answer)
                    {
                        (OutcomeKind kind, string canonical) = OutcomeLine.Parse(answer);
                        outcomes.Add(new ExecutionOutcome(input, culture, kind, canonical));
                    }
                    else
                    {
                        session.Dispose();
                        session = null;
                        outcomes.Add(new ExecutionOutcome(input, culture, OutcomeKind.NotComparable, OutcomeLine.NoAnswer));
                    }
                }
            }
        }
        finally
        {
            session?.Dispose();
        }

        return outcomes;
    }
}
