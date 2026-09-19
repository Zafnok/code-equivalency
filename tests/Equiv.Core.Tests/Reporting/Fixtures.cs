using Equiv.Core.Ir;
using Equiv.Core.Reporting;
using Equiv.Core.Verdicts;

namespace Equiv.Core.Tests.Reporting;

/// <summary>Shared building blocks for <c>Equiv.Core.Reporting</c> tests.</summary>
internal static class Fixtures
{
    public static IrRun Run(int returned = 1) => new(new IrReturned(new IrBitVecValue(32, (ulong)returned)), [], []);

    public static Counterexample Counterexample(int oldReturn = 1, int newReturn = 2) =>
        new(new IrInputs([new IrBitVecValue(32, 0)]), Run(oldReturn), Run(newReturn));

    public static VerificationResult Result(Verdict verdict, string identity = "Samples.Math::Add(int32,int32)") =>
        new(new ProcedureIdentity(identity), verdict);
}
