namespace Equiv.Core.Verdicts;

/// <summary>
/// What kind of difference a Divergent pair has, derived from the three answers of its <see cref="FailureRefinement"/> and
/// nothing else (ADR 0037's clarification of 2026-10-11; ticket P1-020). SARIF <c>properties.divergenceKind</c>.
/// </summary>
public enum DivergenceKind
{
    /// <summary>Some answer is not decided, or none of the three found a difference.</summary>
    Undetermined,

    /// <summary>The modern side returns on an input the legacy side throws on, and the pair differs nowhere else.</summary>
    RemovedFailuresOnly,

    /// <summary>The modern side throws on an input the legacy side returns on, and the pair differs nowhere else.</summary>
    NewFailuresOnly,

    /// <summary>Each side throws on an input the other returns on, and the pair differs nowhere else.</summary>
    FailuresOnly,

    /// <summary>On some input both sides return, or both throw the same exception type, and another observable differs.</summary>
    ResultChanged,
}
