using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Core;

/// <summary>
/// Answers <see cref="IrPure"/> applications for <see cref="IrInterpreter"/> (ADR 0025; ticket M4-002). Must be a function
/// of the function and its arguments: the same question always gets the same answer, whatever the position. In replay the
/// oracle answers from the solver's model.
/// </summary>
public interface IPureOracle
{
    /// <summary>
    /// Returns <paramref name="pure"/>'s result on <paramref name="arguments"/>: a value of its target's type, and one
    /// flag per entry of its <see cref="IrPure.Throws"/>, in order.
    /// </summary>
    IrPureResult Answer(IrPure pure, ImmutableArray<IrValue> arguments);
}
