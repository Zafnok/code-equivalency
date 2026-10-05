namespace Equiv.Core;

/// <summary>
/// What an <see cref="ISmtSolver"/> said of a script (ADR 0050 decision 1). Closed: <see cref="SmtSat"/>,
/// <see cref="SmtUnsat"/>, <see cref="SmtUnknown"/>.
/// </summary>
public abstract record SmtAnswer
{
    private protected SmtAnswer()
    {
    }
}
