namespace Equiv.Execute;

/// <summary>One running driver process: one JSON line in, one JSON line out, per case.</summary>
public interface IDriverSession : IDisposable
{
    /// <summary>
    /// Sends one case and returns the driver's answer, or null when the process went over its memory limit or exited.
    /// Throws <see cref="TimeoutException"/> when sending the case and reading the answer together take longer than
    /// <paramref name="timeout"/>, whether the driver never read the case or never answered it (ticket P2-039). After null
    /// or a timeout the session is dead.
    /// </summary>
    /// <exception cref="TimeoutException">No answer within <paramref name="timeout"/>.</exception>
    string? Exchange(string line, TimeSpan timeout);
}
