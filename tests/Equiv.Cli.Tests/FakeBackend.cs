using Equiv.Core;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

namespace Equiv.Cli.Tests;

/// <summary>A backend returning a canned verdict per pair (keyed by <see cref="ProcedurePair.New"/>'s identity) and recording every <see cref="VerificationOptions"/> it was called with.</summary>
internal sealed class FakeBackend(IReadOnlyDictionary<string, Verdict> verdictByIdentity) : IVerificationBackend
{
    public List<VerificationOptions> Calls { get; } = [];

    public Verdict Verify(ProcedurePair pair, VerificationOptions options)
    {
        Calls.Add(options);
        return verdictByIdentity[pair.New.Value];
    }
}
