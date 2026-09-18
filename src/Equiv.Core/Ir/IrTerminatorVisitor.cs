namespace Equiv.Core.Ir;

/// <summary>Exhaustive dispatch over <see cref="IrTerminator"/>.</summary>
internal abstract class IrTerminatorVisitor<TResult>
{
    public abstract TResult Visit(IrGoto terminator);

    public abstract TResult Visit(IrBranch terminator);

    public abstract TResult Visit(IrSwitch terminator);

    public abstract TResult Visit(IrReturn terminator);

    public abstract TResult Visit(IrThrow terminator);

    public abstract TResult Visit(IrUnreachable terminator);
}
