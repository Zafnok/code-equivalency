namespace Equiv.Core.RuntimeChanges;

/// <summary>
/// One entry of the runtime-changes table's <c>marshalling</c> list (ADR 0058; ticket P2-165): an item of the Interop
/// category of Microsoft's breaking-change pages. <paramref name="Id"/> names it in a runtime rule (<c>extern:</c> and the
/// id), <paramref name="Reach"/> says which imported functions it touches, <paramref name="ChangedIn"/> is the first
/// runtime with the new behaviour, <paramref name="Reason"/> is a one-sentence explanation and <paramref name="Url"/>
/// the page it came from.
/// </summary>
public sealed record MarshallingChange(string Id, MarshallingReach Reach, TargetRuntime ChangedIn, string Reason, Uri Url);
