using Equiv.Core.Ir;

namespace Equiv.Core.Verdicts;

/// <summary>
/// An input the two procedures disagree on, and each side's run (VERIFICATION-MODEL.md section 5;
/// shape from ticket M3-001's model decoder). <see cref="IrRun"/> already bundles outcome, final
/// by-ref values and the call trace, so no separate outcome/trace fields are needed here.
/// </summary>
public sealed record Counterexample(IrInputs Inputs, IrRun Old, IrRun New);
