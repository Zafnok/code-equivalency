namespace Equiv.Core.Configuration;

/// <summary>
/// The two modes of <c>equiv compare</c> (ADR 0049; ticket P1-032). Both run the same first pass. A mode changes
/// budgets, the bound and which queries are asked, never what a verdict claims.
/// </summary>
public enum CompareMode
{
    /// <summary>The default: the first pass, then further passes over the pairs that are still Unknown.</summary>
    Thorough,

    /// <summary>The first pass alone.</summary>
    Quick,
}
