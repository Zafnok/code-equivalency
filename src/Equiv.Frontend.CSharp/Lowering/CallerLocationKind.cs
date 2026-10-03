namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>Which fact about where a call sits the compiler supplies for a parameter (ADR 0046).</summary>
internal enum CallerLocationKind
{
    /// <summary>The calling file's path, for a <c>[CallerFilePath]</c> parameter.</summary>
    File,

    /// <summary>The call's line number, for a <c>[CallerLineNumber]</c> parameter.</summary>
    Line,
}
