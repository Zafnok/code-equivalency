using Equiv.Core;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

namespace Equiv.Cli;

/// <summary>
/// The backend <see cref="Program.Main"/> wires in until <c>Equiv.Verify.Z3</c> exists (M3-001).
/// Unreachable in practice: with no frontend configured, <see cref="FrontendRouter.Route"/> always
/// rejects before any <see cref="ProcedurePair"/> could reach <see cref="Verify"/>.
/// </summary>
internal sealed class NoBackend : IVerificationBackend
{
    public Verdict Verify(ProcedurePair pair, VerificationOptions options) =>
        throw new InvalidOperationException("no verification backend is configured");
}
