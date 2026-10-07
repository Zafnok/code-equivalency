namespace Equiv.Verify.Z3.Refinement;

/// <summary>When rung 1 asks its queries of the product with its hard arithmetic abstracted (<see cref="ArithmeticRefinement"/>; ticket P1-031).</summary>
internal enum ArithmeticMode
{
    /// <summary>When a query of the exact product hit its budget and the pair holds an operation the abstraction replaces.</summary>
    OnTimeout,

    /// <summary>Never: a query that hits its budget leaves rung 1 the timeout it was before the ticket. Only a test sets it.</summary>
    Off,

    /// <summary>Always, and the exact product is not asked: the soundness harness runs every pair this way. Only a test sets it.</summary>
    Forced,
}
