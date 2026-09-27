using Equiv.Core.Execution;

namespace Equiv.Execute;

/// <summary>
/// One driver's cases streamed on stdin, in one process while it answers (tickets M3-032, P1-008). A case that gets no
/// answer is <see cref="OutcomeKind.NotComparable"/>; its process is dropped and the next case starts a new one.
/// </summary>
internal sealed class DriverStream(IDriverHost host, string driver, TimeSpan caseTimeout) : IDisposable
{
    private IDriverSession? session;

    public ExecutionOutcome Run(ExecutionInput input, string culture)
    {
        session ??= host.Start(driver);
        if (session.Exchange(OutcomeLine.Case(culture, input), caseTimeout) is { } answer)
        {
            (OutcomeKind kind, string canonical) = OutcomeLine.Parse(answer);
            return new ExecutionOutcome(input, culture, kind, canonical);
        }

        session.Dispose();
        session = null;
        return new ExecutionOutcome(input, culture, OutcomeKind.NotComparable, OutcomeLine.NoAnswer);
    }

    public void Dispose() => session?.Dispose();
}
