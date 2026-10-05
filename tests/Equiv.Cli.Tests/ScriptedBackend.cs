using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Cli.Tests;

/// <summary>
/// A backend whose answers a test computes from the modern body's identity and the options the call was given, on the
/// thread that asked (ticket P2-077). <see cref="Contracts"/> answers <see cref="VerifyUnderContracts"/>, null unless set.
/// </summary>
internal sealed class ScriptedBackend(Func<string, VerificationOptions, Verdict> verify) : IVerificationBackend
{
    public Func<string, VerificationOptions, Equivalent?> Contracts { get; init; } = static (_, _) => null;

    public Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options) => verify(newBody.Identity.Value, options);

    public Equivalent? VerifyUnderContracts(IrProcedure oldBody, IrProcedure newBody, ImmutableArray<CalleePair> callees, VerificationOptions options) =>
        Contracts(newBody.Identity.Value, options);
}
