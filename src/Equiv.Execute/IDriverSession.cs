namespace Equiv.Execute;

/// <summary>One running driver process: one JSON line in, one JSON line out, per case.</summary>
public interface IDriverSession : IDisposable
{
    /// <summary>
    /// Sends one case and returns the driver's answer, or null when none came within <paramref name="timeout"/>, the
    /// process went over its memory limit, or it exited. After null the session is dead.
    /// </summary>
    string? Exchange(string line, TimeSpan timeout);
}
