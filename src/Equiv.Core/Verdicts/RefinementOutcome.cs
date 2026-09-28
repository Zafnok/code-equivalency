namespace Equiv.Core.Verdicts;

/// <summary>What one failure-refinement query of an Unknown found (ADR 0037; VERIFICATION-MODEL.md section 6).</summary>
public enum RefinementOutcome
{
    /// <summary>Unsatisfiable on every resolution of every unshared opaque node: no such failure exists.</summary>
    NoneProved,

    /// <summary>An input on which neither side reaches an unshared opaque node shows the failure, and its replay is untainted.</summary>
    Found,

    /// <summary>The solver gave up, the model's replay depends on an abstraction, or the failure is possible only through an opaque node.</summary>
    Unknown,
}
