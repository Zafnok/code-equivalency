namespace Equiv.Core.Execution;

/// <summary>
/// The two compiled drivers of one <see cref="ExecutionRequest"/>, each for its side's runtime (ADR 0040 decision 3; ticket
/// P2-056): a .NET Framework executable is run directly, a .NET assembly through <c>dotnet</c>.
/// </summary>
public sealed record ExecutionDrivers(string Legacy, string Modern);
