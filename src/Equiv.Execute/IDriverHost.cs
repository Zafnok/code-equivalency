namespace Equiv.Execute;

/// <summary>The process-running seam (ticket M3-032): unit tests fake it, and only the Windows integration tests start real drivers.</summary>
public interface IDriverHost
{
    /// <summary>Starts <paramref name="driver"/>: a <c>.exe</c> runs directly on .NET Framework 4.8, a <c>.dll</c> through <c>dotnet</c>.</summary>
    IDriverSession Start(string driver);

    /// <summary>
    /// A host whose drivers each start in a fresh working directory under <paramref name="directory"/>, so a relative write
    /// by the code under test lands there and is deleted with it instead of next to the caller (ticket P2-040).
    /// </summary>
    IDriverHost Within(string directory);
}
