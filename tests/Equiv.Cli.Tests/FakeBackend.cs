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
/// </summary>
internal sealed class FakeBackend(IReadOnlyDictionary<string, Verdict> verdictByIdentity, IReadOnlyDictionary<string, Exception>? throwByIdentity = null) : IVerificationBackend
{
    public List<VerificationOptions> Calls { get; } = [];

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options)
    {
        Calls.Add(options);
        if (throwByIdentity is not null && throwByIdentity.TryGetValue(newBody.Identity.Value, out Exception? exception))
        {
            throw exception;
        }

        return verdictByIdentity[newBody.Identity.Value];
    }
}
