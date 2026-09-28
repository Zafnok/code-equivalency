using System.Collections.Frozen;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem;

namespace IlLoweringSpike;

/// <summary>
/// P1-012 criterion 2: the ILAst instruction kinds a small table could lower to constructs the IR already has, each with
/// the IR it would become. A kind is keyed by its <see cref="OpCode"/>, refined by a bracketed context where the same opcode
/// is lowerable in one position and not in another. The table follows the IOperation lowering's own choices: where
/// <c>IOPERATION-COVERAGE.md</c> declines a construct for a semantic reason (unboxing, a caught exception object, a ref
/// local, a lambda whose body is elsewhere), the IL form of it is unmapped too. Only syntax the compiler has already
/// expanded into IL the IR has (a type switch, an interpolated string, an event add, a method group) becomes lowerable.
/// </summary>
internal static class MappingTable
{
    /// <summary>Every mapped key and the IR construct it lowers to. Printed into the report verbatim.</summary>
    public static readonly FrozenDictionary<string, string> Mapped = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ILFunction"] = "the procedure",
        ["BlockContainer"] = "blocks of the CFG (`IrBlock`)",
        ["Block"] = "`IrBlock`",
        ["Nop"] = "nothing",
        ["Branch"] = "`IrGoto`",
        ["Leave"] = "`IrGoto` to the container's exit, or `IrReturn` from the function's",
        ["IfInstruction"] = "`IrBranch`",
        ["SwitchInstruction"] = "`IrSwitch`",
        ["SwitchSection"] = "a case of `IrSwitch`",
        ["TryCatch"] = "the `Try` lowering's exception edges (M4-008)",
        ["TryCatchHandler"] = "a clause the exception edge is routed to by type",
        ["TryFinally"] = "the `finally` blocks copied onto every exit (M4-008)",
        ["TryFault"] = "the fault blocks copied onto the throwing exits",
        ["Throw[new]"] = "`IrThrow` of the constructed type (as `Throw` does)",
        ["LdLoc"] = "an SSA variable",
        ["StLoc"] = "an SSA definition",
        ["LdLoca[address operand]"] = "the receiver or a `ref`/`out` argument (`IrOut`, M4-003)",
        ["LdcI4"] = "`IrConst` bitvector or Bool",
        ["LdcI8"] = "`IrConst` bitvector",
        ["LdcF4"] = "`IrConst` designated `float` element",
        ["LdcF8"] = "`IrConst` designated `double` element",
        ["LdcDecimal"] = "`IrConst` designated `decimal` element",
        ["LdStr"] = "`IrConst` designated `string` element",
        ["LdNull"] = "`IrConst` element 0 of the sort",
        ["DefaultValue"] = "`IrConst` designated default of the sort",
        ["BinaryNumericInstruction"] = "`IrBinary`/`IrOverflows`, or `IrPure` `f32.*`/`f64.*`/`dec.*` (M4-002)",
        ["Comp"] = "`IrBinary` comparison, or `IrPure` for floating point",
        ["LogicNot"] = "`IrUnary` `not`",
        ["Conv"] = "`IrUnary` zext/sext/trunc, or `IrPure` `conv.*` (M4-002)",
        ["Call"] = "`IrCall` with a threw edge",
        ["CallVirt"] = "`IrCall` with a threw edge and the receiver's null check",
        ["NewObj"] = "`IrCall` to the constructor (`ObjectCreation`)",
        ["LdObj"] = "a read of the place its address operand names (field map, array map, SSA variable)",
        ["StObj"] = "a write of the place its address operand names",
        ["LdFlda[address operand]"] = "`field.<Type>.<Field>` map at the receiver",
        ["LdsFlda[address operand]"] = "`field.<Type>.<Field>` map, static",
        ["LdElema[address operand]"] = "`array.<Sort>` map with the bounds check (P1-006)",
        ["AddressOf[address operand]"] = "a temporary for a struct receiver",
        ["LdLen"] = "`length.<Sort>`",
        ["NewArr"] = "the `ArrayCreation` lowering (P2-001)",
        ["IsInst"] = "the type test and `cast.<From>.<To>` read (M4-005)",
        ["CastClass"] = "the downcast: type test, `cast` read, `InvalidCastException` branch (M4-005)",
        ["Box"] = "`cast.<From>.<To>` read of a boxing conversion (M3-010)",
        ["LdTypeToken"] = "the shared `typeof.<T>` input",
        ["LdFtn"] = "`IrConst` designated element naming the method, as a delegate constructor's argument",
        ["LdVirtFtn"] = "as `LdFtn`, after the receiver's null check",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The key of <paramref name="instruction"/>: its opcode, refined by position where the table needs it.</summary>
    public static string Key(ILInstruction instruction, IReadOnlySet<ILVariable> caughtException)
    {
        switch (instruction)
        {
            case LdLoc load when caughtException.Contains(load.Variable) && !IsCarried(load):
                return "LdLoc[caught exception]";
            case LdLoca load when caughtException.Contains(load.Variable):
                return "LdLoca[caught exception]";
            case StLoc store when store.Variable.Kind == VariableKind.Local && store.Variable.Type is ByReferenceType or PointerType:
                return "StLoc[ref local]";
            case LdLoca or LdFlda or LdsFlda or LdElema or AddressOf:
                return $"{instruction.OpCode}[{(IsAddressOperand(instruction) ? "address operand" : "address escapes")}]";
            case Throw @throw:
                return @throw.Argument is NewObj ? "Throw[new]" : "Throw[not new]";
            case NewArr array when array.Indices.Count != 1:
                return "NewArr[rank > 1]";
            case DefaultValue value when value.Type.Kind == TypeKind.TypeParameter:
                return "DefaultValue[type parameter]";
            case NewObj create when IsCompilerGenerated(create.Method):
                return "NewObj[closure class]";
            case CallInstruction call when call.Method.Name.Contains("g__", StringComparison.Ordinal):
                return $"{instruction.OpCode}[local function]";
            case CallInstruction call when IsCompilerGenerated(call.Method):
                return $"{instruction.OpCode}[compiler-generated method]";
            case LdFtn ftn when IsCompilerGenerated(ftn.Method):
                return "LdFtn[lambda]";
            case LdVirtFtn ftn when IsCompilerGenerated(ftn.Method):
                return "LdVirtFtn[lambda]";
            case BinaryNumericInstruction:
                return "BinaryNumericInstruction";
            default:
                return instruction.OpCode.ToString();
        }
    }

    /// <summary>A lambda, local function or other compiler-generated method: its body is elsewhere and its name is an ordinal.</summary>
    private static bool IsCompilerGenerated(IMethod method) =>
        method.Name.StartsWith('<') || method.DeclaringTypeDefinition?.Name.StartsWith('<') == true;

    /// <summary>
    /// A place's address consumed where the IR has the place: read or written through, the target of a field or element
    /// address, or passed as a call's receiver or <c>ref</c>/<c>out</c> argument.
    /// </summary>
    private static bool IsAddressOperand(ILInstruction address) => address.Parent switch
    {
        LdObj or LdFlda or LdElema => address.ChildIndex == 0,
        StObj => address.ChildIndex == 0,
        CallInstruction => true,
        StLoc { Variable.Kind: VariableKind.StackSlot } => true, // a read-modify-write's address, kept once (`a[i] += 1`)
        _ => false,
    };

    /// <summary>The handler's exception slot copied into the catch variable: the copy, not the read, is what matters.</summary>
    private static bool IsCarried(LdLoc load) => load.Parent is StLoc;

    /// <summary>
    /// The variables that hold a caught exception object: each handler's variable, and every local a handler's variable
    /// is copied into, transitively. The IR has no value for it (<c>CaughtException</c> in the coverage table).
    /// </summary>
    public static HashSet<ILVariable> CaughtException(ILFunction function)
    {
        HashSet<ILVariable> held = [.. function.Descendants.OfType<TryCatchHandler>().Select(static h => h.Variable)];
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (StLoc store in function.Descendants.OfType<StLoc>())
            {
                if (store.Value is LdLoc load && held.Contains(load.Variable) && held.Add(store.Variable))
                {
                    grew = true;
                }
            }
        }

        return held;
    }
}
