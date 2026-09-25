namespace Equiv.TestSupport;

/// <summary>What the second array parameter <c>v</c> of an <see cref="OracleMethod"/> is bound to.</summary>
public enum ArrayBinding
{
    /// <summary>An array of its own, <c>{ B, A }</c>.</summary>
    Distinct,

    /// <summary>The same array as <c>u</c> (ticket P1-006).</summary>
    Aliased,

    /// <summary><c>null</c>, so an access through <c>v</c> throws <c>NullReferenceException</c> where the CLR checks it (ticket P2-017).</summary>
    Null,
}
