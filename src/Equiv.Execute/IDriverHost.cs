namespace Equiv.Execute;

/// <summary>The process-running seam (ticket M3-032): unit tests fake it, and only the Windows integration tests start real drivers.</summary>
public interface IDriverHost
{
    /// <summary>Starts <paramref name="driver"/>: a <c>.exe</c> runs directly on .NET Framework 4.8, a <c>.dll</c> through <c>dotnet</c>.</summary>
    IDriverSession Start(string driver);
}
