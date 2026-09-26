namespace Equiv.Core.Execution;

/// <summary>
/// Turns a member into runnable drivers (ADR 0035, ticket M3-032). Implemented by a language frontend, which alone can
/// resolve a member against each runtime's reference assemblies.
/// </summary>
public interface IExecutionDriverFactory
{
    /// <summary>Every public member present on both runtimes whose <see cref="CallIdentity.Value"/> is <paramref name="member"/> or starts with it.</summary>
    IReadOnlyList<ExecutionSignature> Resolve(string member);

    /// <summary>
    /// Compiles one driver per runtime for <paramref name="request"/> into <paramref name="directory"/>. Throws
    /// <see cref="InvalidOperationException"/> when a driver does not compile.
    /// </summary>
    ExecutionDrivers Create(ExecutionRequest request, string directory);
}
