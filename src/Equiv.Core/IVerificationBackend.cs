using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

namespace Equiv.Core;

/// <summary>
/// Compares one matched procedure pair and returns a verdict (ARCHITECTURE.md; VERIFICATION-MODEL.md
/// section 1). <c>Equiv.Verify.Z3</c> implements it (ticket M3-001).
/// </summary>
public interface IVerificationBackend
{
    Verdict Verify(IrProcedure oldBody, IrProcedure newBody, VerificationOptions options);
}
