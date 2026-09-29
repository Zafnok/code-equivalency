using System.Collections.Immutable;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>Each side's call to a callee under contract gets its own outcome and heap (ADR 0036 decision 2).</summary>
internal sealed class FreshPerSideEncoding : ICalleeContractEncoding
{
    private FreshPerSideEncoding()
    {
    }

    public static FreshPerSideEncoding Instance { get; } = new();

    public bool FreshPerSide => true;
}
