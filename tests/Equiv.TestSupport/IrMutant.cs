using Equiv.Core.Ir;

namespace Equiv.TestSupport;

/// <summary>A semantics-changing edit of <paramref name="Original"/>; <paramref name="Witness"/> tells them apart.</summary>
public sealed record IrMutant(IrProcedure Original, IrProcedure Mutant, IrInputs Witness, string Description);
