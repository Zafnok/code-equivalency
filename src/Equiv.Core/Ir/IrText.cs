using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Equiv.Core.Ir;

/// <summary>
/// The IR text format. <see cref="Dump"/> is deterministic (blocks and instructions in
/// procedure order, map entries sorted by their text) and <see cref="Parse"/> inverts it.
/// Definitions carry their type (<c>%t: bv32 = add %a, %b</c>), literals carry theirs
/// (<c>bv32 1</c>, <c>bool true</c>, <c>sort "S" 3</c>, <c>map&lt;bv32, bool&gt; [bv32 1 -> bool true] default bool false</c>),
/// and line breaks are ordinary whitespace. A call's heap pairs follow its <c>threw</c> flag as
/// <c>heap("field.C.x" %before -> %after: map&lt;sort "C", bv32&gt;)</c>, and are left out when there are none.
/// </summary>
public static class IrText
{
    internal static readonly ImmutableArray<(string Name, IrBinaryOp Op)> BinaryNames =
    [
        ("add", IrBinaryOp.Add), ("sub", IrBinaryOp.Sub), ("mul", IrBinaryOp.Mul),
        ("sdiv", IrBinaryOp.SDiv), ("srem", IrBinaryOp.SRem), ("udiv", IrBinaryOp.UDiv), ("urem", IrBinaryOp.URem),
        ("and", IrBinaryOp.And), ("or", IrBinaryOp.Or), ("xor", IrBinaryOp.Xor),
        ("shl", IrBinaryOp.Shl), ("ashr", IrBinaryOp.AShr), ("lshr", IrBinaryOp.LShr),
        ("eq", IrBinaryOp.Eq), ("ne", IrBinaryOp.Ne),
        ("slt", IrBinaryOp.Slt), ("sle", IrBinaryOp.Sle), ("sgt", IrBinaryOp.Sgt), ("sge", IrBinaryOp.Sge),
        ("ult", IrBinaryOp.Ult), ("ule", IrBinaryOp.Ule), ("ugt", IrBinaryOp.Ugt), ("uge", IrBinaryOp.Uge),
    ];

    internal static readonly ImmutableArray<(string Name, IrOverflowOp Op)> OverflowNames =
    [
        ("sadd", IrOverflowOp.SAdd), ("uadd", IrOverflowOp.UAdd), ("ssub", IrOverflowOp.SSub), ("usub", IrOverflowOp.USub),
        ("smul", IrOverflowOp.SMul), ("umul", IrOverflowOp.UMul), ("sdiv", IrOverflowOp.SDiv),
    ];

    internal static readonly ImmutableArray<(string Name, IrUnaryOp Op)> UnaryNames =
    [
        ("neg", IrUnaryOp.Neg), ("not", IrUnaryOp.Not), ("boolnot", IrUnaryOp.BoolNot),
        ("zext", IrUnaryOp.ZExt), ("sext", IrUnaryOp.SExt), ("trunc", IrUnaryOp.Trunc),
    ];

    private static readonly FrozenDictionary<IrBinaryOp, string> BinaryText = BinaryNames.ToFrozenDictionary(static n => n.Op, static n => n.Name);

    private static readonly FrozenDictionary<IrOverflowOp, string> OverflowText = OverflowNames.ToFrozenDictionary(static n => n.Op, static n => n.Name);

    private static readonly FrozenDictionary<IrUnaryOp, string> UnaryText = UnaryNames.ToFrozenDictionary(static n => n.Op, static n => n.Name);

    public static string Dump(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        StringBuilder text = new();
        text.Append("proc ").Append(Quote(procedure.Identity.Value))
            .Append(" (").AppendJoin(", ", procedure.Parameters.Select(Parameter)).Append(')');
        if (procedure.ReturnType is not null)
        {
            text.Append(" -> ").Append(Type(procedure.ReturnType));
        }

        text.Append(" entry ").Append(Block(procedure.Entry)).Append('\n');
        foreach (IrBlock block in procedure.Blocks)
        {
            text.Append(Block(block.Id)).Append(":\n");
            foreach (IrInstruction instruction in block.Instructions)
            {
                text.Append("  ").Append(Line(instruction)).Append('\n');
            }

            text.Append("  ").Append(Line(block.Terminator)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Parses <see cref="Dump"/> output or hand-written IR. Throws <see cref="IrParseException"/> with line and column.</summary>
    public static IrProcedure Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return IrTextParser.Parse(text);
    }

    internal static string Line(IrInstruction instruction) => instruction.Accept(IrInstructionWriter.Instance);

    internal static string Line(IrTerminator terminator) => terminator.Accept(IrTerminatorWriter.Instance);

    internal static string Type(IrType type) => type switch
    {
        IrBool => "bool",
        IrBitVec bitVec => "bv" + Number(bitVec.Width),
        IrSort sort => "sort " + Quote(sort.Name),
        _ => MapType((IrMap)type),
    };

    internal static string Value(IrValue value) => value switch
    {
        IrBoolValue b => b.Value ? "bool true" : "bool false",
        IrBitVecValue v => $"{Type(v.Type)} {v.Bits.ToString(CultureInfo.InvariantCulture)}",
        IrSortValue s => $"{Type(s.Type)} {Number(s.Id)}",
        _ => MapValue((IrMapValue)value),
    };

    internal static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static string MapType(IrMap map) => $"map<{Type(map.Key)}, {Type(map.Value)}>";

    private static string MapValue(IrMapValue map)
    {
        IEnumerable<string> entries = map.Entries
            .Select(static e => $"{Value(e.Key)} -> {Value(e.Value)}")
            .Order(StringComparer.Ordinal);
        return $"{MapType(map.MapType)} [{string.Join(", ", entries)}] default {Value(map.Default)}";
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string Block(IrBlockId id) => "B" + Number(id.Value);

    private static string Use(IrVar var) => "%" + var.Name;

    private static string Definition(IrVar var) =>
        var.SourceName is null
            ? $"%{var.Name}: {Type(var.Type)}"
            : $"%{var.Name} {Quote(var.SourceName)}: {Type(var.Type)}";

    private static string Parameter(IrParameter parameter) => parameter.Kind switch
    {
        IrParameterKind.Ref => "ref " + Definition(parameter.Var),
        IrParameterKind.Out => "out " + Definition(parameter.Var),
        _ => Definition(parameter.Var),
    };

    private sealed class IrInstructionWriter : IIrInstructionVisitor<string>
    {
        public static readonly IrInstructionWriter Instance = new();

        private static string Assigned(IrVar? target) => target is null ? string.Empty : Definition(target) + " = ";

        /// <summary>
        /// A callee's quoted identity, with a <c>!</c> suffix when <see cref="CallIdentity.RuntimeChanged"/> (ticket M2-006)
        /// and an <c>@</c> suffix when <see cref="CallIdentity.External"/> (ticket M3-033).
        /// </summary>
        private static string Callee(CallIdentity callee) =>
            Quote(callee.Value) + (callee.RuntimeChanged ? "!" : string.Empty) + (callee.External ? "@" : string.Empty);

        public string Visit(IrConst instruction) => $"{Definition(instruction.Target)} = const {Value(instruction.Value)}";

        public string Visit(IrBinary instruction) =>
            $"{Definition(instruction.Target)} = {BinaryText[instruction.Op]} {Use(instruction.A)}, {Use(instruction.B)}";

        public string Visit(IrOverflows instruction) =>
            $"{Definition(instruction.Target)} = overflows {OverflowText[instruction.Op]} {Use(instruction.A)}, {Use(instruction.B)}";

        public string Visit(IrUnary instruction) => $"{Definition(instruction.Target)} = {UnaryText[instruction.Op]} {Use(instruction.A)}";

        public string Visit(IrPhi instruction) =>
            $"{Definition(instruction.Target)} = phi [{string.Join(", ", instruction.Incoming.Select(static i => $"{Block(i.From)}: {Use(i.Value)}"))}]";

        public string Visit(IrCall instruction) =>
            $"{Assigned(instruction.Target)}call {Callee(instruction.Callee)}({string.Join(", ", instruction.Args.Select(Use))})"
            + (instruction.Threw is null ? string.Empty : " threw " + Definition(instruction.Threw))
            + (instruction.Heap.IsEmpty ? string.Empty : $" heap({string.Join(", ", instruction.Heap.Select(static h => $"{Quote(h.Map)} {Use(h.Before)} -> {Definition(h.After)}"))})");

        public string Visit(IrMapRead instruction) =>
            $"{Definition(instruction.Target)} = mapread {Use(instruction.Map)}, {Use(instruction.Key)}";

        public string Visit(IrMapWrite instruction) =>
            $"{Definition(instruction.Target)} = mapwrite {Use(instruction.Map)}, {Use(instruction.Key)}, {Use(instruction.Value)}";

        /// <summary>
        /// <c>%t: T = pure "f"(%a, %b)</c>, the function with a <c>!</c> suffix when <see cref="IrPure.RuntimeSensitive"/>, then
        /// <c>throws(%f: bool "E", ...)</c> when it can throw (ticket M4-002).
        /// </summary>
        public string Visit(IrPure instruction) =>
            $"{Definition(instruction.Target)} = pure {Quote(instruction.Function)}{(instruction.RuntimeSensitive ? "!" : string.Empty)}({string.Join(", ", instruction.Args.Select(Use))})"
            + (instruction.Throws.IsEmpty ? string.Empty : $" throws({string.Join(", ", instruction.Throws.Select(static t => $"{Definition(t.Flag)} {Quote(t.ExceptionType)}"))})");

        public string Visit(IrOpaque instruction)
        {
            SourceSpan span = instruction.Span;
            return $"{Assigned(instruction.Target)}opaque {(instruction.WholeBody ? "body " : string.Empty)}{Quote(instruction.Reason)} at {Quote(span.Path)} "
                + $"{Number(span.StartLine)}:{Number(span.StartColumn)}-{Number(span.EndLine)}:{Number(span.EndColumn)}";
        }
    }

    private sealed class IrTerminatorWriter : IIrTerminatorVisitor<string>
    {
        public static readonly IrTerminatorWriter Instance = new();

        private static string Outs(ImmutableArray<IrOut> outs) =>
            outs.IsEmpty ? string.Empty : $" outs({string.Join(", ", outs.Select(static o => $"{Use(o.Param)} = {Use(o.Final)}"))})";

        public string Visit(IrGoto terminator) => "goto " + Block(terminator.Target);

        public string Visit(IrBranch terminator) =>
            $"br {Use(terminator.Cond)}, {Block(terminator.Then)}, {Block(terminator.Else)}";

        public string Visit(IrSwitch terminator) =>
            $"switch {Use(terminator.Scrutinee)} [{string.Join(", ", terminator.Cases.Select(static c => $"{Value(c.Value)} -> {Block(c.Target)}"))}] default {Block(terminator.Default)}";

        public string Visit(IrReturn terminator) =>
            (terminator.Value is null ? "ret" : "ret " + Use(terminator.Value)) + Outs(terminator.Outs);

        public string Visit(IrThrow terminator) => "throw " + Quote(terminator.ExceptionType) + Outs(terminator.Outs);

        public string Visit(IrUnreachable terminator) => "unreachable";
    }
}
