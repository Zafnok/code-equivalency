namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// A fact about the method that makes a call that the row can fire on (ticket P2-114). A row that
/// <see cref="RuntimeChange.Requires"/> one never fires in a method for which it does not hold. A row names it by the
/// text <see cref="RuntimeChangeTable"/> parses (<c>runtime-changes.json</c>'s <c>requires</c>). Every value but
/// <see cref="AsyncStreamOperation"/> is a fact about the method's calls to the row's own member, a call to a forwarder to
/// it among them: it holds when one of them can reach the row's change (ticket P2-190).
/// </summary>
public enum RowPrecondition
{
    /// <summary>The method calls <c>ReadAsync</c>, <c>WriteAsync</c>, <c>BeginRead</c>, <c>BeginWrite</c>, <c>CopyToAsync</c> or <c>FlushAsync</c> on a stream; JSON <c>asyncStreamOperation</c>.</summary>
    AsyncStreamOperation,

    /// <summary>
    /// The method reads the member's <c>OperatingSystem</c> other than as <c>.Platform</c>, as <c>.Version</c> compared
    /// with a constant two-part version no newer than 6.2, which the compatibility shim's 6.2.9200 never crosses, or as
    /// <c>.Version.Major</c> compared with a constant that gives the same answer for the shim's 6 as for every major from 6 up
    /// (ticket P2-216); JSON <c>shimmedVersionRead</c>.
    /// </summary>
    ShimmedVersionRead,

    /// <summary>The method calls the member and does not convert its result to an integer type at once, which turns -0 and +0 into the same 0; JSON <c>signedZeroResult</c>.</summary>
    SignedZeroResult,

    /// <summary>
    /// The method calls the member on a field the method does not show is an instance field: the result of
    /// <c>Type.GetField(string, BindingFlags)</c>, or an element of <c>Type.GetFields(BindingFlags)</c>, with constant
    /// flags that include <c>Instance</c> and not <c>Static</c>; JSON <c>staticFieldSet</c>.
    /// </summary>
    StaticFieldSet,

    /// <summary>
    /// The method calls the member on an encoding the method does not show is <c>Encoding.ASCII</c>, with bytes the method
    /// does not show are constants that form well-formed UTF-8 (ticket P2-216); JSON <c>utf8Decoding</c>.
    /// </summary>
    Utf8Decoding,

    /// <summary>
    /// The method calls the member other than with a <c>ProcessStartInfo</c> the method shows has <c>UseShellExecute</c>
    /// set and <c>WindowStyle</c> left alone; JSON <c>defaultShellExecute</c>.
    /// </summary>
    DefaultShellExecute,

    /// <summary>
    /// The method reads the member on a <c>Process</c> it does not show it created with <c>new</c>: only a process this code
    /// did not start, one <c>GetProcesses</c> or <c>GetProcessById</c> returns, has no <c>StartInfo</c> to read (ticket
    /// P2-216); JSON <c>foreignProcess</c>.
    /// </summary>
    ForeignProcess,

    /// <summary>
    /// The method sets the member to a value it does not show is at least -2, <c>ImageList.Indexer.NoneIndex</c>, below which
    /// alone .NET throws (ticket P2-216); JSON <c>belowNoneIndex</c>.
    /// </summary>
    BelowNoneIndex,
}
