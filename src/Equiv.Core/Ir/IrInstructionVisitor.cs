namespace Equiv.Core.Ir;

/// <summary>Exhaustive dispatch over <see cref="IrInstruction"/>; adding a kind breaks every visitor at compile time.</summary>
internal abstract class IrInstructionVisitor<TResult>
{
    public abstract TResult Visit(IrConst instruction);

    public abstract TResult Visit(IrBinary instruction);

    public abstract TResult Visit(IrOverflows instruction);

    public abstract TResult Visit(IrUnary instruction);

    public abstract TResult Visit(IrPhi instruction);

    public abstract TResult Visit(IrCall instruction);

    public abstract TResult Visit(IrMapRead instruction);

    public abstract TResult Visit(IrMapWrite instruction);

    public abstract TResult Visit(IrOpaque instruction);
}
