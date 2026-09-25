using System.Collections.Immutable;
using System.Globalization;

using CsCheck;

using Equiv.Core;
using Equiv.Core.Ir;

using static Equiv.TestSupport.IrGenAst;

namespace Equiv.TestSupport;

/// <summary>
/// CsCheck generators for IR. Procedures come from a small structured language (sequence,
/// if/else, switch, bounded counter loops, checked arithmetic, opaque calls, pure functions, throws) lowered
/// to SSA by <see cref="IrGenLowering"/>, so they are well formed by construction.
/// </summary>
public static class IrGen
{
    /// <summary>Enough steps for every generated procedure (loops run at most 3 x 3 x 3 times).</summary>
    public const int StepBudget = 100_000;

    private static readonly uint[] Edges = [0, 1, 2, 0x7F, 0x80, 0xFF, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF];

    private static readonly IrBinaryOp[] Arithmetic =
    [
        IrBinaryOp.Add, IrBinaryOp.Sub, IrBinaryOp.Mul, IrBinaryOp.SDiv, IrBinaryOp.SRem, IrBinaryOp.UDiv, IrBinaryOp.URem,
        IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Shl, IrBinaryOp.AShr, IrBinaryOp.LShr,
    ];

    private static readonly IrBinaryOp[] ComparisonOps =
    [
        IrBinaryOp.Eq, IrBinaryOp.Ne, IrBinaryOp.Slt, IrBinaryOp.Sle, IrBinaryOp.Sgt, IrBinaryOp.Sge,
        IrBinaryOp.Ult, IrBinaryOp.Ule, IrBinaryOp.Ugt, IrBinaryOp.Uge,
    ];

    private static readonly IrBinaryOp[] Commutative =
    [
        IrBinaryOp.Add, IrBinaryOp.Mul, IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Eq, IrBinaryOp.Ne,
    ];

    private static readonly Gen<uint> Word = Gen.Frequency(
        (2, Gen.OneOfConst(Edges)),
        (3, Gen.UInt[0, 16]),
        (1, Gen.UInt));

    /// <summary>
    /// Well-formed procedures: two bv32 parameters, sometimes a bv32 <c>ref</c> parameter, sometimes a
    /// <c>ref</c> heap map <c>field.Gen.x</c>, bv32 result.
    /// </summary>
    public static Gen<IrProcedure> Procedure { get; } = Procedures(loops: true);

    /// <summary>As <see cref="Procedure"/> without loops, so every CFG is acyclic (the M3-001 encoder's domain).</summary>
    public static Gen<IrProcedure> AcyclicProcedure { get; } = Procedures(loops: false);

    /// <summary>
    /// An acyclic procedure and the same procedure with its source-language parameters renamed, which a
    /// caller cannot tell apart (ADR 0021).
    /// </summary>
    public static Gen<(IrProcedure Original, IrProcedure Renamed)> AcyclicRenamedPair { get; } =
        Programs(loops: false).Select(static p => (IrGenLowering.Lower(p), IrGenLowering.Lower(p, renamed: true)));

    /// <summary>One procedure per validator rule, each breaking exactly that rule. Each has the heap map, which the heap-pair rules need.</summary>
    public static Gen<IrViolation> Violations { get; } =
        Gen.Select(Programs(loops: true).Select(static p => IrGenLowering.Lower(p with { HasHeap = true })), Gen.Int[0, 11], static (procedure, rule) => Violate(procedure, rule));

    /// <summary>Inputs matching <paramref name="procedure"/>'s parameters (bitvector and Bool only).</summary>
    public static Gen<IrInputs> Inputs(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        Gen<IrValue>[] values = [.. procedure.Parameters.Select(static p => Value(p.Var.Type))];
        return Sequence(values).Select(static v => new IrInputs(v));
    }

    /// <summary>
    /// Semantics-changing edits of <paramref name="procedure"/>: swap the two bv32 parameters in the
    /// signature only (a caller binds them by position, ADR 0021), swap the operands of a
    /// non-commutative operation, flip a branch, change a constant, insert an opaque statement at
    /// the start of a block (ADR 0014), drop or change a heap map write, or duplicate a call (ADR
    /// 0018; calls are not idempotent). An edit is kept only when some input (edge values plus
    /// random ones) makes the interpreter observe a difference; otherwise the generator discards it
    /// and yields null. A caller may re-mutate an already-mutated procedure (a stacked pair): an edit's
    /// derived variable name (<c>.dup</c>, <c>.kept</c>, ...) is fixed to the instruction it targets, so
    /// re-selecting the same instruction on the second pass can collide with a name the first pass
    /// already introduced. Rather than have every edit track uniqueness across stacking, a mutant that
    /// turns out not to validate is discarded the same as one with no observable difference.
    /// </summary>
    public static Gen<IrMutant?> Mutation(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        List<(string Description, Func<IrProcedure> Apply)> edits = Edits(procedure);
        if (edits.Count == 0)
        {
            return Gen.Const((IrMutant?)null);
        }

        ImmutableArray<IrInputs> edgeInputs = EdgeInputs(procedure);
        return Gen.Select(Gen.Int[0, edits.Count - 1], Inputs(procedure).Array[16], (index, random) =>
        {
            IrProcedure mutant = edits[index].Apply();
            if (!IrValidator.Validate(mutant).IsEmpty)
            {
                return null;
            }

            IrInputs? witness = edgeInputs.Concat(random).FirstOrDefault(input => Run(procedure, input) != Run(mutant, input));
            return witness is null ? null : new IrMutant(procedure, mutant, witness, edits[index].Description);
        });
    }

    public static IrRun Run(IrProcedure procedure, IrInputs inputs) => IrInterpreter.Run(procedure, inputs, IrGenOracle.Instance, StepBudget, pure: IrGenOracle.Instance);

    private static Gen<ImmutableArray<IrValue>> Sequence(Gen<IrValue>[] values) =>
        values.Aggregate(
            Gen.Const(ImmutableArray<IrValue>.Empty),
            static (acc, value) => Gen.Select(acc, value, static (a, v) => a.Add(v)));

    private static Gen<IrProcedure> Procedures(bool loops) => Programs(loops).Select(static p => IrGenLowering.Lower(p));

    private static Gen<Program> Programs(bool loops) =>
        Gen.Select(Gen.Bool, Gen.Bool, Word.Array[4], Statements(2, loops), Expression(2), static (hasRef, hasHeap, inits, body, result) =>
            new Program(hasRef, hasHeap, [.. inits], body, result));

    private static Gen<IrValue> Value(IrType type) => type switch
    {
        IrBool => Gen.Bool.Select(static b => (IrValue)new IrBoolValue(b)),
        IrMap map => Gen.Select(Word, Gen.Select(Word, Word).Array[0, 3], (fallback, entries) => (IrValue)Map(map, fallback, entries)),
        IrBitVec { Width: 32 } => Word.Select(static w => (IrValue)new IrBitVecValue(32, w)),
        IrBitVec bitVec => Gen.ULong.Select(u => (IrValue)new IrBitVecValue(bitVec.Width, u & (ulong.MaxValue >> (64 - bitVec.Width)))),
        _ => throw new NotSupportedException($"No input generator for {type}."),
    };

    private static ImmutableArray<IrInputs> EdgeInputs(IrProcedure procedure)
    {
        IEnumerable<ImmutableArray<IrValue>> combinations = [[]];
        foreach (IrParameter parameter in procedure.Parameters)
        {
            IrValue[] choices = parameter.Var.Type switch
            {
                IrBitVec { Width: 32 } => [.. Edges.Select(static e => (IrValue)new IrBitVecValue(32, e))],
                IrMap map => [Map(map, 0, []), Map(map, 0xFFFF_FFFF, [(0, 1), (1, 0x8000_0000)])],
                _ => [new IrBoolValue(Value: false), new IrBoolValue(Value: true)],
            };
            combinations = [.. combinations.SelectMany(c => choices.Select(c.Add))];
        }

        return [.. combinations.Select(static c => new IrInputs(c))];
    }

    private static IrMapValue Map(IrMap type, uint fallback, (uint Key, uint Value)[] entries) =>
        new(
            type,
            new IrBitVecValue(32, fallback),
            entries.DistinctBy(static e => e.Key).ToImmutableDictionary(static e => (IrValue)new IrBitVecValue(32, e.Key), static e => (IrValue)new IrBitVecValue(32, e.Value)));

    private static List<(string, Func<IrProcedure>)> Edits(IrProcedure procedure)
    {
        // Every generated procedure starts with the bv32 parameters a and b.
        List<(string, Func<IrProcedure>)> edits =
        [
            ("swap parameters a and b", () => procedure with { Parameters = procedure.Parameters.SetItem(0, procedure.Parameters[1]).SetItem(1, procedure.Parameters[0]) }),
        ];
        for (int b = 0; b < procedure.Blocks.Length; b++)
        {
            IrBlock block = procedure.Blocks[b];
            int firstBody = block.Instructions.TakeWhile(static i => i is IrPhi).Count();
            int opaqueBlock = b;
            edits.Add(($"insert opaque in {block.Id}", () =>
                InsertInstructions(procedure, opaqueBlock, firstBody, new IrOpaque(Target: null, "mutant", new SourceSpan("gen", 1, 1, 1, 1)))));
            for (int i = 0; i < block.Instructions.Length; i++)
            {
                int blockIndex = b;
                int index = i;
                switch (block.Instructions[i])
                {
                    case IrMapWrite write:
                        {
                            IrVar kept = new(write.Target.Name + Fresh(procedure, ".kept"), write.Value.Type);
                            IrVar one = new(write.Target.Name + Fresh(procedure, ".one"), write.Value.Type);
                            IrVar changed = new(write.Target.Name + Fresh(procedure, ".changed"), write.Value.Type);
                            edits.Add(($"drop map write {write.Target.Name}", () =>
                                InsertInstructions(ReplaceInstruction(procedure, blockIndex, index, write with { Value = kept }), blockIndex, index, new IrMapRead(kept, write.Map, write.Key))));
                            edits.Add(($"change map write {write.Target.Name}", () =>
                                InsertInstructions(
                                    ReplaceInstruction(procedure, blockIndex, index, write with { Value = changed }),
                                    blockIndex,
                                    index,
                                    new IrConst(one, new IrBitVecValue(32, 1)),
                                    new IrBinary(changed, IrBinaryOp.Add, write.Value, one))));
                            break;
                        }

                    case IrCall call:
                        edits.Add(($"duplicate call {index.ToString(CultureInfo.InvariantCulture)} in {block.Id}", () =>
                            InsertInstructions(procedure, blockIndex, index, Duplicate(procedure, call))));
                        break;
                    case IrBinary binary when !Commutative.Contains(binary.Op) && binary.A != binary.B:
                        edits.Add(($"swap operands of {binary.Target.Name}", () =>
                            ReplaceInstruction(procedure, blockIndex, index, binary with { A = binary.B, B = binary.A })));
                        break;
                    case IrConst { Value: IrBitVecValue bits } constant:
                        edits.Add(($"change constant {constant.Target.Name}", () =>
                            ReplaceInstruction(procedure, blockIndex, index, constant with { Value = new IrBitVecValue(bits.Width, (bits.Bits + 1) & (ulong.MaxValue >> (64 - bits.Width))) })));
                        break;
                }
            }

            if (block.Terminator is IrBranch branch && branch.Then != branch.Else)
            {
                int blockIndex = b;
                edits.Add(($"flip branch in {block.Id}", () =>
                    ReplaceBlock(procedure, blockIndex, block with { Terminator = branch with { Then = branch.Else, Else = branch.Then } })));
            }
        }

        return edits;
    }

    private static IrProcedure ReplaceInstruction(IrProcedure procedure, int blockIndex, int index, IrInstruction instruction)
    {
        IrBlock block = procedure.Blocks[blockIndex];
        return ReplaceBlock(procedure, blockIndex, block with { Instructions = block.Instructions.SetItem(index, instruction) });
    }

    private static IrProcedure InsertInstructions(IrProcedure procedure, int blockIndex, int index, params IrInstruction[] instructions)
    {
        IrBlock block = procedure.Blocks[blockIndex];
        return ReplaceBlock(procedure, blockIndex, block with { Instructions = block.Instructions.InsertRange(index, instructions) });
    }

    /// <summary>
    /// <paramref name="suffix"/>, lengthened until no variable of <paramref name="procedure"/> contains it, so an edit of a
    /// procedure that is already a mutant derives names no earlier edit used.
    /// </summary>
    private static string Fresh(IrProcedure procedure, string suffix)
    {
        string text = IrText.Dump(procedure);
        while (text.Contains(suffix, StringComparison.Ordinal))
        {
            suffix += "m";
        }

        return suffix;
    }

    /// <summary>A copy of <paramref name="call"/> that defines fresh names; its heap versions are left unused.</summary>
    private static IrCall Duplicate(IrProcedure procedure, IrCall call)
    {
        string suffix = Fresh(procedure, ".dup");
        return call with
        {
            Target = Renamed(call.Target, suffix),
            Threw = Renamed(call.Threw, suffix),
            Heap = [.. call.Heap.Select(h => h with { After = Renamed(h.After, suffix)! })],
        };
    }

    private static IrVar? Renamed(IrVar? var, string suffix) => var is null ? null : var with { Name = var.Name + suffix };

    private static IrProcedure ReplaceBlock(IrProcedure procedure, int blockIndex, IrBlock block) =>
        procedure with { Blocks = procedure.Blocks.SetItem(blockIndex, block) };

    private static IrViolation Violate(IrProcedure procedure, int rule)
    {
        IrVar a = procedure.Parameters[0].Var;
        IrBitVec bv32 = new(32);
        IrBlock entry = procedure.Blocks[0];
        IrVar bad = new("bad", bv32);
        IrBlock Extra(params IrInstruction[] instructions) =>
            new(new IrBlockId(procedure.Blocks.Length), [.. instructions], new IrUnreachable());
        IrProcedure WithExtra(IrBlock block) => procedure with { Blocks = procedure.Blocks.Add(block) };
        IrVar map = procedure.Parameters.Single(static p => p.Var.Type is IrMap).Var;
        IrHeapPair Pair(string after) => new(map.Name, map, new IrVar(after, map.Type));

        return rule switch
        {
            0 => new(WithExtra(new IrBlock(procedure.Entry, [], new IrUnreachable())), IrDiagnosticIds.DuplicateBlockId),
            1 => new(procedure with { Entry = new IrBlockId(9999) }, IrDiagnosticIds.MissingEntry),
            2 => new(ReplaceBlock(procedure, 0, entry with { Instructions = entry.Instructions.Insert(1, entry.Instructions[0]) }), IrDiagnosticIds.MultipleAssignment),
            3 => new(
                ReplaceBlock(procedure, 0, entry with { Instructions = entry.Instructions.Insert(0, new IrUnary(bad, IrUnaryOp.Neg, ((IrConst)entry.Instructions[0]).Target)) }),
                IrDiagnosticIds.UseNotDominated),
            4 => new(WithExtra(Extra(new IrPhi(bad, [(procedure.Entry, a)]))), IrDiagnosticIds.PhiPredecessors),
            5 => new(WithExtra(Extra(new IrConst(bad, new IrBitVecValue(32, 0)), new IrPhi(new IrVar("bad2", bv32), []))), IrDiagnosticIds.PhiPlacement),
            6 => new(WithExtra(Extra(new IrBinary(new IrVar("bad", new IrBool()), IrBinaryOp.Add, a, a))), IrDiagnosticIds.OperandTypes),
            7 => new(WithExtra(new IrBlock(new IrBlockId(procedure.Blocks.Length), [], new IrGoto(new IrBlockId(9999)))), IrDiagnosticIds.MissingTarget),
            8 => new(WithExtra(Extra(new IrMapRead(bad, a, a))), IrDiagnosticIds.MapTypes),
            9 => new(AddOut(procedure, new IrOut(a, a)), IrDiagnosticIds.ExitOuts),
            10 => new(WithExtra(Extra(new IrCall(Target: null, Threw: null, new CallIdentity("Svc::F"), []) { Heap = [Pair("bad"), Pair("bad2")] })), IrDiagnosticIds.HeapPairRepeated),
            _ => new(WithExtra(Extra(new IrCall(Target: null, Threw: null, new CallIdentity("Svc::F"), []) { Heap = [new IrHeapPair("field.Gen.none", a, bad)] })), IrDiagnosticIds.HeapPairMap),
        };
    }

    private static IrProcedure AddOut(IrProcedure procedure, IrOut extra)
    {
        int index = procedure.Blocks.ToList().FindIndex(static b => b.Terminator is IrReturn or IrThrow);
        IrBlock block = procedure.Blocks[index];
        IrTerminator terminator = block.Terminator is IrReturn exit
            ? exit with { Outs = exit.Outs.Add(extra) }
            : (IrThrow)block.Terminator with { Outs = ((IrThrow)block.Terminator).Outs.Add(extra) };
        return ReplaceBlock(procedure, index, block with { Terminator = terminator });
    }

    private static Gen<IExpr> Expression(int depth)
    {
        Gen<IExpr> leaf = Gen.Frequency(
            (3, Gen.Int[0, SlotCount - 1].Select(static i => (IExpr)new Slot(i))),
            (1, Word.Select(static w => (IExpr)new Literal(w))));
        if (depth == 0)
        {
            return leaf;
        }

        Gen<IExpr> smaller = Expression(depth - 1);
        return Gen.Frequency(
            (3, leaf),
            (3, Gen.Select(Gen.OneOfConst(Arithmetic), smaller, smaller, static (op, l, r) => (IExpr)new Binary(op, l, r))),
            (1, Gen.Select(Gen.OneOfConst(IrUnaryOp.Neg, IrUnaryOp.Not), smaller, static (op, e) => (IExpr)new Unary(op, e))),
            (1, Gen.Select(Gen.Bool, smaller, static (signed, e) => (IExpr)new Narrow(signed, e))),
            (1, smaller.Select(static k => (IExpr)new Load(k))));
    }

    private static Gen<ICond> Condition(int depth)
    {
        Gen<ICond> compare = Gen.Select(Gen.OneOfConst(ComparisonOps), Expression(1), Expression(1), static (op, l, r) => (ICond)new Compare(op, l, r));
        if (depth == 0)
        {
            return compare;
        }

        Gen<ICond> smaller = Condition(depth - 1);
        return Gen.Frequency(
            (4, compare),
            (1, Gen.Select(Gen.OneOfConst(IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Eq, IrBinaryOp.Ne), smaller, smaller, static (op, l, r) => (ICond)new Logic(op, l, r))),
            (1, smaller.Select(static c => (ICond)new Negate(c))));
    }

    private static Gen<ImmutableArray<IStmt>> Statements(int depth, bool loops) =>
        Statement(depth, loops).Array[0, 3].Select(static s => s.ToImmutableArray());

    private static Gen<IStmt> Statement(int depth, bool loops)
    {
        Gen<int> slot = Gen.Int[0, SlotCount - 1];
        Gen<IStmt> assign = Gen.Select(slot, Expression(2), static (s, e) => (IStmt)new Assign(s, e));
        Gen<IStmt> check = Gen.Select(
            slot,
            Gen.OneOfConst(IrOverflowOp.SAdd, IrOverflowOp.UAdd, IrOverflowOp.SSub, IrOverflowOp.USub, IrOverflowOp.SMul, IrOverflowOp.UMul, IrOverflowOp.SDiv),
            Expression(1),
            Expression(1),
            static (s, op, l, r) => (IStmt)new Checked(s, op, l, r));
        Gen<IStmt> call = Gen.Select(
            Gen.Frequency((3, slot.Select(static s => (int?)s)), (1, Gen.Const((int?)null))),
            Gen.OneOfConst("Svc::F", "Svc::G"),
            Expression(1).Array[0, 2],
            Gen.Bool,
            static (s, callee, args, mayThrow) => (IStmt)new Call(s, callee, [.. args], mayThrow));
        Gen<IStmt> store = Gen.Select(Expression(1), Expression(1), static (k, v) => (IStmt)new Store(k, v));
        Gen<IStmt> pure = Gen.Select(
            slot,
            Gen.OneOfConst("gen.f", "gen.g"),
            Expression(1).Array[1, 2],
            Gen.OneOfConst<ImmutableArray<string>>([], ["System.OverflowException"], ["System.DivideByZeroException", "System.OverflowException"]),
            static (s, function, args, throws) => (IStmt)new Pure(s, function, [.. args], throws));
        if (depth == 0)
        {
            return Gen.Frequency((4, assign), (1, check), (1, call), (1, store), (1, pure));
        }

        Gen<ImmutableArray<IStmt>> body = Statements(depth - 1, loops);
        Gen<ImmutableArray<IStmt>> maybeThrowing = Gen.Select(body, Gen.Int[0, 3], static (s, k) =>
            k == 0 ? s.Add(new Throw("System.InvalidOperationException")) : s);
        Gen<IStmt> branch = Gen.Select(Condition(1), maybeThrowing, body, static (c, t, e) => (IStmt)new If(c, t, e));
        Gen<IStmt> choice = Gen.Select(
            Expression(1),
            Gen.Select(Word, body, static (v, b) => (v, b)).Array[0, 3],
            body,
            static (e, cases, fallback) => (IStmt)new Switch(e, [.. cases], fallback));
        Gen<IStmt> loop = Gen.Select(Gen.Int[0, 3], body, static (n, b) => (IStmt)new Loop(n, b));
        return Gen.Frequency((4, assign), (1, check), (1, call), (1, store), (1, pure), (2, branch), (1, choice), (loops ? 1 : 0, loop));
    }
}
