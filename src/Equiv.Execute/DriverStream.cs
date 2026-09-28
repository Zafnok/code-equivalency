using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// One driver's cases streamed on stdin, in one process while it answers (tickets M3-032, P1-008). A case that gets no
/// answer is <see cref="OutcomeKind.NotComparable"/>; its process is dropped and the next case starts a new one. A timeout's
/// canonical form is <see cref="OutcomeLine.TimedOut"/>, so callers can tell a driver that hangs from one that crashed
/// (ticket P2-039).
/// </summary>
internal sealed class DriverStream(IDriverHost host, string driver, TimeSpan caseTimeout) : IDisposable
{
    private IDriverSession? session;

    public ExecutionOutcome Run(ExecutionInput input, string culture)
    {
        session ??= host.Start(driver);
        string? answer;
        try
        {
            answer = session.Exchange(OutcomeLine.Case(culture, input), caseTimeout);
        }
        catch (TimeoutException)
        {
            return Dropped(input, culture, OutcomeLine.TimedOut(caseTimeout));
        }

        if (answer is null)
        {
            return Dropped(input, culture, OutcomeLine.NoAnswer);
        }

        (OutcomeKind kind, string canonical) = OutcomeLine.Parse(answer);
        return new ExecutionOutcome(input, culture, kind, canonical);
    }

    public void Dispose() => session?.Dispose();

    private ExecutionOutcome Dropped(ExecutionInput input, string culture, string canonical)
    {
        session?.Dispose();
        session = null;
        return new ExecutionOutcome(input, culture, OutcomeKind.NotComparable, canonical);
    }
}
