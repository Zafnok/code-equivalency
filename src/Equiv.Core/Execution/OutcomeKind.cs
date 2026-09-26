namespace Equiv.Core.Execution;

/// <summary>How one case ended on one runtime (ADR 0035, ticket M3-032).</summary>
public enum OutcomeKind
{
    /// <summary>The member returned; the canonical form is its value.</summary>
    Returned,

    /// <summary>The member threw; the canonical form is the exception type's full name only.</summary>
    Threw,

    /// <summary>The member returned a value with no canonical form, or gave no answer in time; never a divergence.</summary>
    NotComparable,

    /// <summary>The case's arguments or culture could not be built on this runtime.</summary>
    NotConstructible,
}
