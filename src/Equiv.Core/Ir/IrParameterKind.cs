namespace Equiv.Core.Ir;

/// <summary>How a parameter is passed. <see cref="Ref"/> and <see cref="Out"/> have observable final values.</summary>
public enum IrParameterKind
{
    In,
    Ref,
    Out,
}
