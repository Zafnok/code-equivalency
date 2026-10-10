namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// A fact about the method that makes a call that the row can fire on (ticket P2-114). A row that
/// <see cref="RuntimeChange.Requires"/> one never fires in a method for which it does not hold. A row names it by the
/// text <see cref="RuntimeChangeTable"/> parses (<c>runtime-changes.json</c>'s <c>requires</c>).
/// </summary>
public enum RowPrecondition
{
    /// <summary>The method calls <c>ReadAsync</c>, <c>WriteAsync</c>, <c>BeginRead</c>, <c>BeginWrite</c>, <c>CopyToAsync</c> or <c>FlushAsync</c> on a stream; JSON <c>asyncStreamOperation</c>.</summary>
    AsyncStreamOperation,
}
