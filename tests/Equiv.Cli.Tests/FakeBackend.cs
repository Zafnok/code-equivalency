using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Cli.Tests;

/// <summary>
/// A backend returning a canned verdict per pair (keyed by the modern body's identity) and recording every
/// <see cref="VerificationOptions"/> it was called with. <paramref name="throwByIdentity"/> lets a ticket M3-013
/// test give one identity a crash instead of a verdict (a missing canned verdict throws
/// <see cref="KeyNotFoundException"/> too, but only <paramref name="throwByIdentity"/> lets the exception itself
/// be chosen, e.g. <see cref="OperationCanceledException"/> or <see cref="OutOfMemoryException"/>).
/// <see cref="Contracts"/> gives a caller's canned result under callee contracts (ticket P1-010), null when missing, and
/// <see cref="ContractFailure"/> is thrown from that call instead when set; <see cref="ContractCalls"/> records each call.
/// </summary>
internal sealed class FakeBackend(IReadOnlyDictionary<string, Verdict> verdictByIdentity, IReadOnlyDictionary<string, Exception>? throwByIdentity = null) : IVerificationBackend
{
    public List<VerificationOptions> Calls { get; } = [];

    public IReadOnlyDictionary<string, Equivalent> Contracts { get; init; } = new Dictionary<string, Equivalent>(StringComparer.Ordinal);

    public Exception? ContractFailure { get; init; }

    public List<(string Caller, ImmutableArray<string> Callees)> ContractCalls { get; } = [];

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
    {
        Calls.Add(options);
        return throwByIdentity is not null && throwByIdentity.TryGetValue(newBody.Identity.Value, out Exception? exception)
            ? throw exception
            : verdictByIdentity[newBody.Identity.Value];
    }

    public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options)
    {
        ContractCalls.Add((newBody.Identity.Value, [.. callees.Select(static c => c.Identity)]));
        return ContractFailure is { } failure ? throw failure : Contracts.GetValueOrDefault(newBody.Identity.Value);
    }
}
