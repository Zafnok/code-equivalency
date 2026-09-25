using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>Exhaustive dispatch over <see cref="IrInstruction"/>; adding a kind breaks every visitor at compile time.</summary>
internal interface IIrInstructionVisitor<out TResult>
{
    TResult Visit(IrConst instruction);

    TResult Visit(IrBinary instruction);

    TResult Visit(IrOverflows instruction);

    TResult Visit(IrUnary instruction);

    TResult Visit(IrPhi instruction);

    TResult Visit(IrCall instruction);

    TResult Visit(IrMapRead instruction);

    TResult Visit(IrMapWrite instruction);

    TResult Visit(IrPure instruction);

    TResult Visit(IrOpaque instruction);
}
