using System.Collections.Frozen;
using System.Linq;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

namespace Equiv.Frontend.CSharp.Lowering.Il;

/// <summary>
/// The ILAst key of an instruction (P1-012's mapping table, ADR 0039): its <see cref="OpCode"/>, refined by a bracketed
/// context where the same opcode is lowerable in one position and not in another. The spelling is the spike's
/// (<c>tools/spikes/il-lowering/MappingTable.cs</c>), so an opaque's reason names the row of <c>docs/tickets/IL-COVERAGE.md</c>
/// that covers it. <see cref="Lowered"/> is the subset <see cref="IlLowerer"/> lowers: every key the spike's table maps
/// (tickets P1-014 and P1-015). Every other key is an <see cref="Equiv.Core.Ir.IrOpaque"/>.
/// </summary>
internal static class IlKeys
{
    /// <summary>The keys <see cref="IlLowerer"/> lowers; an instruction with any other key is opaque with its key as reason.</summary>
    public static readonly FrozenSet<string> Lowered = new[]
    {
        "ILFunction", "BlockContainer", "Block", "Nop", "Branch", "Leave", "IfInstruction", "SwitchInstruction", "SwitchSection",
        "TryCatch", "TryCatchHandler", "TryFinally", "TryFault", "Throw[new]",
        "LdLoc", "StLoc", "LdLoca[address operand]",
        "LdcI4", "LdcI8", "LdcF4", "LdcF8", "LdcDecimal", "LdStr", "LdNull", "DefaultValue",
        "BinaryNumericInstruction", "Comp", "Conv",
        "Call", "CallVirt", "NewObj",
        "LdObj", "StObj", "LdFlda[address operand]", "LdsFlda[address operand]", "LdElema[address operand]", "LdLen", "NewArr", "AddressOf[address operand]",
        "IsInst", "CastClass", "Box", "LdTypeToken", "LdFtn", "LdVirtFtn",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The instructions that take an address, whose key says whether the address is used where the IR has its place.</summary>
    private static readonly FrozenSet<OpCode> Addresses = new[] { OpCode.LdLoca, OpCode.LdFlda, OpCode.LdsFlda, OpCode.LdElema, OpCode.AddressOf }.ToFrozenSet();

    /// <summary>The key of <paramref name="instruction"/>: its opcode, refined by position where the table needs it.</summary>
    public static string Key(ILInstruction instruction, IReadOnlySet<ILVariable> caughtException) => instruction switch
    {
        LdLoc load when caughtException.Contains(load.Variable) && load.Parent is not StLoc => "LdLoc[caught exception]",
        LdLoca load when caughtException.Contains(load.Variable) => "LdLoca[caught exception]",
        StLoc store when store.Variable.Kind == VariableKind.Local && store.Variable.Type is ByReferenceType or PointerType => "StLoc[ref local]",
        _ when Addresses.Contains(instruction.OpCode) => $"{instruction.OpCode}[{(IsAddressOperand(instruction) ? "address operand" : "address escapes")}]",
        Throw @throw => @throw.Argument is NewObj ? "Throw[new]" : "Throw[not new]",
        NewArr array when array.Indices.Count != 1 => "NewArr[rank > 1]",
        DefaultValue value when value.Type.Kind == TypeKind.TypeParameter => "DefaultValue[type parameter]",
        NewObj create when IsCompilerGenerated(create.Method) => "NewObj[closure class]",
        CallInstruction call when call.Method.Name.Contains("g__", StringComparison.Ordinal) => $"{instruction.OpCode}[local function]",
        CallInstruction call when IsCompilerGenerated(call.Method) => $"{instruction.OpCode}[compiler-generated method]",
        LdFtn ftn when IsCompilerGenerated(ftn.Method) => "LdFtn[lambda]",
        BinaryNumericInstruction => "BinaryNumericInstruction",
        _ => instruction.OpCode.ToString(),
    };

    /// <summary>
    /// The variables that hold a caught exception object: each handler's variable, and every local a handler's variable is
    /// copied into, transitively. The IR has no value for it (<c>CaughtException</c> in the coverage table).
    /// </summary>
    public static HashSet<ILVariable> CaughtException(ILFunction function)
    {
        HashSet<ILVariable> held = [];
        foreach (TryCatchHandler handler in function.Descendants.OfType<TryCatchHandler>())
        {
            held.Add(handler.Variable);
        }

        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (StLoc store in function.Descendants.OfType<StLoc>())
            {
                grew |= store.Value is LdLoc load && held.Contains(load.Variable) && held.Add(store.Variable);
            }
        }

        return held;
    }

    /// <summary>A lambda, local function or other compiler-generated method: its body is elsewhere and its name is an ordinal.</summary>
    private static bool IsCompilerGenerated(IMethod method) =>
        method.Name.StartsWith('<') || method.DeclaringTypeDefinition?.Name.StartsWith('<') == true;

    /// <summary>
    /// A place's address consumed where the IR has the place: read or written through, the target of a field or element
    /// address, passed as a call's receiver or <c>ref</c>/<c>out</c> argument, or kept once for a read-modify-write.
    /// </summary>
    private static bool IsAddressOperand(ILInstruction address) => address.Parent switch
    {
        LdObj or LdFlda or LdElema or StObj => address.ChildIndex == 0,
        CallInstruction => true,
        StLoc { Variable.Kind: VariableKind.StackSlot } => true,
        _ => false,
    };
}
