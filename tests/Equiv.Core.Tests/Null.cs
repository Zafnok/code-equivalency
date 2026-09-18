namespace Equiv.Core.Tests;

/// <summary>
/// Returns null through an opaque generic call so tests can exercise a record's strongly-typed
/// <c>Equals(T?)</c> null branch without CA1508 flagging a literal <c>x.Equals(null)</c> as
/// provably-false dead code.
/// </summary>
internal static class Null
{
    public static T? Of<T>()
        where T : class => null;
}
