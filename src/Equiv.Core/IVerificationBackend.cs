using Equiv.Core.Matching;
using Equiv.Core.Verdicts;

namespace Equiv.Core;

/// <summary>
/// Compares one matched procedure pair and returns a verdict (ARCHITECTURE.md). Stubbed here;
/// <c>Equiv.Verify.Z3</c> implements it against Z3 (ticket M3-001), which may still adjust this
/// signature once lowering has a real place in the pipeline (the matcher's <see cref="ProcedurePair"/>
/// carries identities only; verifying needs each side's <c>IrProcedure</c> too).
/// </summary>
public interface IVerificationBackend
{
    Verdict Verify(ProcedurePair pair, VerificationOptions options);
}
