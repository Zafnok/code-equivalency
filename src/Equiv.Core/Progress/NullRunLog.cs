namespace Equiv.Core.Progress;

/// <summary>The <see cref="IRunLog"/> that writes nothing: the default of <see cref="VerificationOptions.Log"/>, and what tests pass.</summary>
public sealed class NullRunLog : IRunLog
{
    private NullRunLog()
    {
    }

    public static NullRunLog Instance { get; } = new();

    public bool IsDebug => false;

    public void Phase(string name, int total, long totalWeight, PhaseBound? bound = null)
    {
        // Nothing to report to.
    }

    public void Item(string identity, long weight)
    {
        // Nothing to report to.
    }

    public void ItemDone(string outcome)
    {
        // Nothing to report to.
    }

    public void Detail(string text)
    {
        // Nothing to report to.
    }

    public void PhaseDone()
    {
        // Nothing to report to.
    }
}
