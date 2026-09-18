namespace Equiv.Core;

/// <summary>A 1-based line/column range in a source file.</summary>
public sealed record SourceSpan(string Path, int StartLine, int StartColumn, int EndLine, int EndColumn);
