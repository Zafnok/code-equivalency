using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>Exhaustive dispatch over <see cref="IrTerminator"/>.</summary>
internal interface IIrTerminatorVisitor<out TResult>
{
    TResult Visit(IrGoto terminator);

    TResult Visit(IrBranch terminator);

    TResult Visit(IrSwitch terminator);

    TResult Visit(IrReturn terminator);

    TResult Visit(IrThrow terminator);

    TResult Visit(IrUnreachable terminator);
}
