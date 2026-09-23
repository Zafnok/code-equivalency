namespace Equiv.Core;

/// <summary>
/// The analysed line count of each codebase in a comparison (ticket M3-014; README "Licence"; VERIFICATION-MODEL.md
/// section 6). The licence's Additional Use Grant measures each codebase separately, so the two counts are only
/// ever reported side by side: there is deliberately no total. How a frontend counts is stated once, in the
/// README next to the licence summary, and applies to both sides identically.
/// </summary>
public sealed record AnalysedLines(int Legacy, int Modern);
