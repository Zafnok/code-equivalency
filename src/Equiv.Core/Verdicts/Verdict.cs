namespace Equiv.Core.Verdicts;

/// <summary>
/// The result for one procedure (VERIFICATION-MODEL.md section 1; ARCHITECTURE.md). Closed:
/// <see cref="Equivalent"/>, <see cref="Divergent"/>, <see cref="Unknown"/>, <see cref="Added"/>,
/// <see cref="Removed"/>. Nothing else.
/// </summary>
public abstract record Verdict
{
    private protected Verdict()
    {
    }
}
