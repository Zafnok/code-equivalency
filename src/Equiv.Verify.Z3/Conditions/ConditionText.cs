using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core.Ir;

using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3.Conditions;

/// <summary>
/// The two renderings of an admitted condition (ADR 0048 decision 4; ticket P1-022), both from the one
/// <see cref="ConditionTerm"/>, so neither depends on how Z3 prints a term. <see cref="Smt"/> is SMT-LIB over the product's
/// inputs, named as the product names them (<see cref="SharedParameter.InputName"/>), and is the exact statement.
/// <see cref="Source"/> is the source spelling, with each parameter's name on the modern side. It reads as C# when the
/// parameters are <c>int</c>, <c>long</c>, <c>bool</c> or references: an unsigned operation casts its operands, and a
/// widening that C# makes implicitly is not written. The IR does not say whether a parameter was declared unsigned or
/// narrower than <c>int</c>, so for such a parameter the text can read differently from the SMT-LIB.
/// </summary>
internal static class ConditionText
{
    private static readonly FrozenDictionary<IrBinaryOp, (string Smt, string Source)> Operators = new Dictionary<IrBinaryOp, (string, string)>
    {
        [IrBinaryOp.Add] = ("bvadd", "+"),
        [IrBinaryOp.Sub] = ("bvsub", "-"),
        [IrBinaryOp.Mul] = ("bvmul", "*"),
        [IrBinaryOp.SDiv] = ("bvsdiv", "/"),
        [IrBinaryOp.SRem] = ("bvsrem", "%"),
        [IrBinaryOp.UDiv] = ("bvudiv", "/"),
        [IrBinaryOp.URem] = ("bvurem", "%"),
        [IrBinaryOp.And] = ("bvand", "&"),
        [IrBinaryOp.Or] = ("bvor", "|"),
        [IrBinaryOp.Xor] = ("bvxor", "^"),
        [IrBinaryOp.Shl] = ("bvshl", "<<"),
        [IrBinaryOp.AShr] = ("bvashr", ">>"),
        [IrBinaryOp.LShr] = ("bvlshr", ">>>"),
        [IrBinaryOp.Eq] = ("=", "=="),
        [IrBinaryOp.Ne] = ("distinct", "!="),
        [IrBinaryOp.Slt] = ("bvslt", "<"),
        [IrBinaryOp.Sle] = ("bvsle", "<="),
        [IrBinaryOp.Sgt] = ("bvsgt", ">"),
        [IrBinaryOp.Sge] = ("bvsge", ">="),
        [IrBinaryOp.Ult] = ("bvult", "<"),
        [IrBinaryOp.Ule] = ("bvule", "<="),
        [IrBinaryOp.Ugt] = ("bvugt", ">"),
        [IrBinaryOp.Uge] = ("bvuge", ">="),
    }.ToFrozenDictionary();

    /// <summary>Each comparison's negation, so that a negated comparison is spelled as one comparison.</summary>
    private static readonly FrozenDictionary<IrBinaryOp, IrBinaryOp> Negated = new Dictionary<IrBinaryOp, IrBinaryOp>
    {
        [IrBinaryOp.Eq] = IrBinaryOp.Ne,
        [IrBinaryOp.Ne] = IrBinaryOp.Eq,
        [IrBinaryOp.Slt] = IrBinaryOp.Sge,
        [IrBinaryOp.Sle] = IrBinaryOp.Sgt,
        [IrBinaryOp.Sgt] = IrBinaryOp.Sle,
        [IrBinaryOp.Sge] = IrBinaryOp.Slt,
        [IrBinaryOp.Ult] = IrBinaryOp.Uge,
        [IrBinaryOp.Ule] = IrBinaryOp.Ugt,
        [IrBinaryOp.Ugt] = IrBinaryOp.Ule,
        [IrBinaryOp.Uge] = IrBinaryOp.Ult,
    }.ToFrozenDictionary();

    /// <summary>
    /// The operations that read their operands as unsigned and have a signed twin with the same spelling: a constant
    /// operand is spelled unsigned, and any other is cast to the unsigned type of its width, which is what makes the
    /// source operator the unsigned one.
    /// </summary>
    private static readonly FrozenSet<IrBinaryOp> Unsigned =
        new[] { IrBinaryOp.UDiv, IrBinaryOp.URem, IrBinaryOp.Ult, IrBinaryOp.Ule, IrBinaryOp.Ugt, IrBinaryOp.Uge }.ToFrozenSet();

    private static readonly FrozenSet<IrBinaryOp> Shifts = new[] { IrBinaryOp.Shl, IrBinaryOp.AShr, IrBinaryOp.LShr }.ToFrozenSet();

    /// <summary>The disjunction of <paramref name="disjuncts"/> in SMT-LIB: the one term, or <c>(or ...)</c> of them.</summary>
    public static string Smt(ImmutableArray<ConditionTerm> disjuncts, ImmutableArray<SharedParameter> shared) =>
        disjuncts.Length == 1 ? Smt(disjuncts[0], shared) : $"(or {string.Join(' ', disjuncts.Select(d => Smt(d, shared)))})";

    /// <summary>The disjunction of <paramref name="disjuncts"/> in source spelling, joined by <c>||</c>.</summary>
    public static string Source(ImmutableArray<ConditionTerm> disjuncts, ImmutableArray<SharedParameter> shared) =>
        string.Join(" || ", disjuncts.Select(d => Source(d, shared)));

    internal static string Smt(ConditionTerm term, ImmutableArray<SharedParameter> shared) => term switch
    {
        ConditionTerm.Input input => Symbol(shared[input.Index].InputName),
        ConditionTerm.Null isNull => $"(select {Symbol(shared[isNull.Map].InputName)} {Smt(isNull.Reference, shared)})",
        ConditionTerm.Constant { Value: IrBoolValue boolean } => boolean.Value ? "true" : "false",
        ConditionTerm.Constant constant => string.Create(CultureInfo.InvariantCulture, $"(_ bv{((IrBitVecValue)constant.Value).Bits} {((IrBitVecValue)constant.Value).Width})"),
        ConditionTerm.Not not => $"(not {Smt(not.A, shared)})",
        ConditionTerm.Unary unary => $"({SmtUnary(unary)} {Smt(unary.A, shared)})",
        _ => SmtBinary((ConditionTerm.Binary)term, shared),
    };

    internal static string Source(ConditionTerm term, ImmutableArray<SharedParameter> shared, bool unsigned = false) => term switch
    {
        ConditionTerm.Input input => shared[input.Index].New!.Var.SourceName ?? shared[input.Index].New!.Var.Name,
        ConditionTerm.Null isNull => $"{Source(isNull.Reference, shared)} == null",
        ConditionTerm.Constant { Value: IrBoolValue boolean } => boolean.Value ? "true" : "false",
        ConditionTerm.Constant constant when unsigned => ((IrBitVecValue)constant.Value).Bits.ToString(CultureInfo.InvariantCulture),
        ConditionTerm.Constant constant => ((IrBitVecValue)constant.Value).TwosComplement.ToString(CultureInfo.InvariantCulture),
        ConditionTerm.Not { A: ConditionTerm.Null isNull } => $"{Source(isNull.Reference, shared)} != null",
        ConditionTerm.Not { A: ConditionTerm.Binary comparison } when ConditionTerm.IsComparison(comparison.Op) => Source(comparison with { Op = Negated[comparison.Op] }, shared),
        ConditionTerm.Not not => "!" + Operand(not.A, shared, unsigned: false),
        ConditionTerm.Unary { Op: IrUnaryOp.Neg } unary => "-" + Operand(unary.A, shared, unsigned: false),
        ConditionTerm.Unary { Op: IrUnaryOp.Not } unary => "~" + Operand(unary.A, shared, unsigned: false),
        ConditionTerm.Unary { Op: IrUnaryOp.ZExt } unary => UnsignedCast(unary.A.Type) + Operand(unary.A, shared, unsigned: false),
        ConditionTerm.Unary unary => Source(unary.A, shared, unsigned),
        _ => SourceBinary((ConditionTerm.Binary)term, shared),
    };

    private static string SmtBinary(ConditionTerm.Binary binary, ImmutableArray<SharedParameter> shared)
    {
        // The three logical operations have a Bool and a bitvector form, told apart by the operands' type.
        string name = binary.Type is IrBool && !ConditionTerm.IsComparison(binary.Op) ? Operators[binary.Op].Smt[2..] : Operators[binary.Op].Smt;
        return $"({name} {Smt(binary.A, shared)} {Smt(binary.B, shared)})";
    }

    private static string SmtUnary(ConditionTerm.Unary unary)
    {
        int extended = ((IrBitVec)unary.Type).Width - ((IrBitVec)unary.A.Type).Width;
        return unary.Op switch
        {
            IrUnaryOp.Neg => "bvneg",
            IrUnaryOp.Not => "bvnot",
            IrUnaryOp.ZExt => string.Create(CultureInfo.InvariantCulture, $"(_ zero_extend {extended})"),
            _ => string.Create(CultureInfo.InvariantCulture, $"(_ sign_extend {extended})"),
        };
    }

    private static string SourceBinary(ConditionTerm.Binary binary, ImmutableArray<SharedParameter> shared)
    {
        bool unsigned = Unsigned.Contains(binary.Op);
        return $"{Operand(binary.A, shared, unsigned)} {Operators[binary.Op].Source} {Operand(Shifts.Contains(binary.Op) ? Count(binary.B) : binary.B, shared, unsigned)}";
    }

    /// <summary>
    /// A shift's count as the source has it: an <c>int</c>, which the IR widens to the width of the shifted value. The
    /// widening is not part of the spelling, and a cast would make the count a type C# does not shift by.
    /// </summary>
    private static ConditionTerm Count(ConditionTerm count) => count is ConditionTerm.Unary { Op: IrUnaryOp.ZExt or IrUnaryOp.SExt } widened ? widened.A : count;

    /// <summary>A term as an operand: in parentheses when its spelling has an operator between two operands.</summary>
    private static string Operand(ConditionTerm term, ImmutableArray<SharedParameter> shared, bool unsigned)
    {
        string text = Source(term, shared, unsigned);
        string operand = text.Contains(' ', StringComparison.Ordinal) ? $"({text})" : text;
        return unsigned && term is not ConditionTerm.Constant ? UnsignedCast(term.Type) + operand : operand;
    }

    /// <summary>The cast to the unsigned C# type as wide as <paramref name="type"/>, one of the four widths a bitvector has.</summary>
    private static string UnsignedCast(IrType type) => ((IrBitVec)type).Width switch
    {
        8 => "(byte)",
        16 => "(ushort)",
        32 => "(uint)",
        _ => "(ulong)",
    };

    /// <summary>An SMT-LIB symbol: <paramref name="name"/> as it is when it is a simple symbol, else quoted with bars.</summary>
    private static string Symbol(string name) =>
        name.All(static c => char.IsAsciiLetterOrDigit(c) || "~!@$%^&*_-+=<>.?/".Contains(c, StringComparison.Ordinal)) ? name : $"|{name}|";
}
