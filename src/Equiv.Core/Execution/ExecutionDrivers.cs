namespace Equiv.Core.Execution;

/// <summary>
/// The two compiled drivers of one <see cref="ExecutionRequest"/>: <see cref="Legacy"/> is a .NET Framework 4.8 executable
/// run directly, <see cref="Modern"/> a .NET 10 assembly run through <c>dotnet</c>.
/// </summary>
public sealed record ExecutionDrivers(string Legacy, string Modern);
