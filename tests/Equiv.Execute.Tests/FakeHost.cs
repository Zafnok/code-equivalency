namespace Equiv.Execute.Tests;

/// <summary>A driver host that answers from a function of (driver, session number, line) and records what it saw.</summary>
internal sealed class FakeHost(Func<string, int, string, string?> answer) : IDriverHost
{
    private readonly Func<string, int, string, string?> answer = answer;

    public List<string> Starts { get; } = [];

    public List<(string Driver, int Session, string Line)> Exchanges { get; } = [];

    public int Disposed { get; private set; }

    public IDriverSession Start(string driver)
    {
        Starts.Add(driver);
        return new Session(this, driver, Starts.Count);
    }

    private sealed class Session(FakeHost host, string driver, int number) : IDriverSession
    {
        public string? Exchange(string line, TimeSpan timeout)
        {
            host.Exchanges.Add((driver, number, line));
            return host.answer(driver, number, line);
        }

        public void Dispose() => host.Disposed++;
    }
}
