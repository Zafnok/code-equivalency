namespace Equiv.Cli;

/// <summary>One count per side of a comparison, as the lowering census reports it (ticket M3-014).</summary>
internal sealed record SideCounts(int Legacy, int Modern);
