using Equiv.Core;
using Equiv.Core.Execution;

namespace Equiv.Execute.Tests;

/// <summary>A driver factory over fixed signatures; <see cref="Create"/> throws for a member named in <paramref name="broken"/>.</summary>
internal sealed class FakeFactory(IReadOnlyList<ExecutionSignature> signatures, string? broken = null) : IExecutionDriverFactory
{
    public List<ExecutionRequest> Requests { get; } = [];

    public IReadOnlyList<ExecutionSignature> Resolve(string member) =>
        [.. signatures.Where(s => s.Member.Value.StartsWith(member, StringComparison.Ordinal))];

    public ExecutionDrivers Create(ExecutionRequest request, string directory)
    {
        if (string.Equals(request.Member.Value, broken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("error CS0619: obsolete");
        }

        Requests.Add(request);
        return new ExecutionDrivers(Path.Combine(directory, "legacy.exe"), Path.Combine(directory, "modern.dll"));
    }

    public static ExecutionSignature Signature(string member, params ExecutionParameter[] parameters) =>
        new(new CallIdentity(member), parameters, [.. parameters.Where(static p => p.Kind == ExecutionTypeKind.Unsupported).Select(static p => p.TypeName)]);

    public static ExecutionParameter Parameter(ExecutionTypeKind kind, string typeName = "T") => new(typeName, kind, []);
}
