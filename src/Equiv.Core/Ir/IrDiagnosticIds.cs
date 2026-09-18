namespace Equiv.Core.Ir;

/// <summary>Validator rule ids; one rule per id.</summary>
public static class IrDiagnosticIds
{
    /// <summary>Two blocks share an id.</summary>
    public const string DuplicateBlockId = "IR001";

    /// <summary>The entry block does not exist.</summary>
    public const string MissingEntry = "IR002";

    /// <summary>A variable (or parameter) is assigned more than once.</summary>
    public const string MultipleAssignment = "IR003";

    /// <summary>A use is undefined, differs from its definition, or is not dominated by it.</summary>
    public const string UseNotDominated = "IR004";

    /// <summary>A phi's incoming blocks are not exactly the block's predecessors.</summary>
    public const string PhiPredecessors = "IR005";

    /// <summary>A phi follows a non-phi instruction, or sits in the entry block.</summary>
    public const string PhiPlacement = "IR006";

    /// <summary>Operand or result types do not fit the operation.</summary>
    public const string OperandTypes = "IR007";

    /// <summary>A terminator targets a block that does not exist.</summary>
    public const string MissingTarget = "IR008";

    /// <summary>Map read/write key, value or map types do not line up.</summary>
    public const string MapTypes = "IR009";

    /// <summary>An exit's outs are not every by-ref parameter once, in declaration order, with matching types.</summary>
    public const string ExitOuts = "IR010";
}
