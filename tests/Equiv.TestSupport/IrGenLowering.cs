using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Ir;

using static Equiv.TestSupport.IrGenAst;

namespace Equiv.TestSupport;

/// <summary>
/// A tiny SSA builder: lowers an <see cref="IrGenAst.Program"/> block by block, keeping the
/// current SSA version of each slot, inserting phis at joins and at loop headers.
/// </summary>
internal sealed class IrGenLowering
{
    private static readonly IrBitVec Bv32 = new(32);
    private static readonly IrBitVec Bv8 = new(8);
    private static readonly IrBool Bool = new();

    private readonly List<Pending> blocks = [];
    private readonly string[] names;
    private readonly IrParameter? byRef;
    private IrVar[] env;
    private Pending? current;
    private int counter;

    private IrGenLowering(bool hasRef)
    {
        names = ["a", "b", hasRef ? "r" : "c", "v0", "v1", "v2"];
        env = [.. names.Select(static n => new IrVar(n, Bv32, n))];
        byRef = hasRef ? new IrParameter(env[2], IrParameterKind.Ref) : null;
    }

    public static IrProcedure Lower(Program program)
    {
        IrGenLowering lowering = new(program.HasRef);
        return lowering.Run(program);
    }

    private IrProcedure Run(Program program)
    {
        ImmutableArray<IrParameter> parameters =
        [
            new(env[0], IrParameterKind.In),
            new(env[1], IrParameterKind.In),
            .. byRef is null ? [] : new[] { byRef },
        ];
        Enter(NewBlock());
        for (int slot = byRef is null ? 2 : 3; slot < SlotCount; slot++)
        {
            IrVar init = new($"{names[slot]}.{counter++}", Bv32, names[slot]);
            Emit(new IrConst(init, new IrBitVecValue(32, program.Inits[slot - 2])));
            env[slot] = init;
        }

        LowerAll(program.Body);
        if (current is not null)
        {
            // Fold every slot into the result so that most edits are observable (keeps the mutation discard rate low).
            IrVar result = Lower(program.Result);
            for (int slot = 0; slot < SlotCount; slot++)
            {
                IrVar mixed = Temp(Bv32);
                Emit(new IrBinary(mixed, slot % 2 == 0 ? IrBinaryOp.Xor : IrBinaryOp.Add, result, env[slot]));
                result = mixed;
            }

            Seal(new IrReturn(result, Outs()));
        }

        return new IrProcedure(new ProcedureIdentity("Gen.Type::M"), parameters, Bv32, [.. blocks.Select(static b => b.Build())], new IrBlockId(0));
    }

    private ImmutableArray<IrOut> Outs() => byRef is null ? [] : [new IrOut(byRef.Var, env[2])];

    private Pending NewBlock()
    {
        Pending block = new(new IrBlockId(blocks.Count));
        blocks.Add(block);
        return block;
    }

    private void Enter(Pending block) => current = block;

    private void Seal(IrTerminator terminator)
    {
        current!.Terminator = terminator;
        current = null;
    }

    private void Emit(IrInstruction instruction) => current!.Body.Add(instruction);

    private IrVar Temp(IrType type) => new($"t{counter++}", type);

    private IrVar Version(int slot) => new($"{names[slot]}.{counter++}", Bv32, names[slot]);

    private IrVar Constant(uint value)
    {
        IrVar target = Temp(Bv32);
        Emit(new IrConst(target, new IrBitVecValue(32, value)));
        return target;
    }

    private IrVar Lower(Expr expr)
    {
        switch (expr)
        {
            case Slot slot:
                return env[slot.Index];
            case Literal literal:
                return Constant(literal.Value);
            case Binary binary:
                {
                    IrVar left = Lower(binary.Left);
                    IrVar right = Lower(binary.Right);
                    IrVar target = Temp(Bv32);
                    Emit(new IrBinary(target, binary.Op, left, right));
                    return target;
                }

            case Unary unary:
                {
                    IrVar operand = Lower(unary.Operand);
                    IrVar target = Temp(Bv32);
                    Emit(new IrUnary(target, unary.Op, operand));
                    return target;
                }

            case Narrow narrow:
                {
                    IrVar operand = Lower(narrow.Operand);
                    IrVar small = Temp(Bv8);
                    Emit(new IrUnary(small, IrUnaryOp.Trunc, operand));
                    IrVar target = Temp(Bv32);
                    Emit(new IrUnary(target, narrow.Signed ? IrUnaryOp.SExt : IrUnaryOp.ZExt, small));
                    return target;
                }

            default:
                throw new System.Diagnostics.UnreachableException(expr.GetType().Name);
        }
    }

    private IrVar Lower(Cond cond)
    {
        switch (cond)
        {
            case Compare compare:
                {
                    IrVar left = Lower(compare.Left);
                    IrVar right = Lower(compare.Right);
                    IrVar target = Temp(Bool);
                    Emit(new IrBinary(target, compare.Op, left, right));
                    return target;
                }

            case Logic logic:
                {
                    IrVar left = Lower(logic.Left);
                    IrVar right = Lower(logic.Right);
                    IrVar target = Temp(Bool);
                    Emit(new IrBinary(target, logic.Op, left, right));
                    return target;
                }

            case Negate negate:
                {
                    IrVar operand = Lower(negate.Operand);
                    IrVar target = Temp(Bool);
                    Emit(new IrUnary(target, IrUnaryOp.BoolNot, operand));
                    return target;
                }

            default:
                throw new System.Diagnostics.UnreachableException(cond.GetType().Name);
        }
    }

    private void LowerAll(ImmutableArray<Stmt> statements)
    {
        foreach (Stmt statement in statements)
        {
            if (current is null)
            {
                return;
            }

            Lower(statement);
        }
    }

    private void Lower(Stmt statement)
    {
        switch (statement)
        {
            case Assign assign:
                env[assign.Slot] = Lower(assign.Value);
                break;
            case Checked check:
                LowerChecked(check);
                break;
            case Call call:
                LowerCall(call);
                break;
            case If branch:
                LowerIf(branch);
                break;
            case Switch choice:
                LowerSwitch(choice);
                break;
            case Loop loop:
                LowerLoop(loop);
                break;
            case Throw exit:
                Seal(new IrThrow(exit.ExceptionType, Outs()));
                break;
            default:
                throw new System.Diagnostics.UnreachableException(statement.GetType().Name);
        }
    }

    private void ThrowIf(IrVar condition, string exceptionType)
    {
        Pending thrown = NewBlock();
        Pending normal = NewBlock();
        Seal(new IrBranch(condition, thrown.Id, normal.Id));
        Enter(thrown);
        Seal(new IrThrow(exceptionType, Outs()));
        Enter(normal);
    }

    private void LowerChecked(Checked check)
    {
        IrVar left = Lower(check.Left);
        IrVar right = Lower(check.Right);
        IrVar overflows = Temp(Bool);
        Emit(new IrOverflows(overflows, check.Op, left, right));
        ThrowIf(overflows, "System.OverflowException");
        IrBinaryOp op = check.Op switch
        {
            IrOverflowOp.SAdd or IrOverflowOp.UAdd => IrBinaryOp.Add,
            IrOverflowOp.SSub or IrOverflowOp.USub => IrBinaryOp.Sub,
            IrOverflowOp.SMul or IrOverflowOp.UMul => IrBinaryOp.Mul,
            _ => IrBinaryOp.SDiv,
        };
        IrVar target = Temp(Bv32);
        Emit(new IrBinary(target, op, left, right));
        env[check.Slot] = target;
    }

    private void LowerCall(Call call)
    {
        ImmutableArray<IrVar> args = [.. call.Args.Select(Lower)];
        IrVar? target = call.Slot is null ? null : Temp(Bv32);
        IrVar? threw = call.MayThrow ? Temp(Bool) : null;
        Emit(new IrCall(target, threw, new CallIdentity(call.Callee), args));
        if (threw is not null)
        {
            ThrowIf(threw, "System.Exception");
        }

        if (call.Slot is int slot)
        {
            env[slot] = target!;
        }
    }

    private void LowerIf(If branch)
    {
        IrVar condition = Lower(branch.Condition);
        Pending then = NewBlock();
        Pending otherwise = NewBlock();
        Seal(new IrBranch(condition, then.Id, otherwise.Id));
        IrVar[] before = [.. env];
        Join([Arm(then, branch.Then, before), Arm(otherwise, branch.Else, before)]);
    }

    private void LowerSwitch(Switch choice)
    {
        IrVar scrutinee = Lower(choice.Scrutinee);
        List<Pending> cases = [.. choice.Cases.Select(_ => NewBlock())];
        Pending fallback = NewBlock();
        Seal(new IrSwitch(
            scrutinee,
            [.. choice.Cases.Select((c, i) => ((IrValue)new IrBitVecValue(32, c.Value), cases[i].Id))],
            fallback.Id));
        IrVar[] before = [.. env];
        List<(Pending? End, IrVar[] Env)> arms = [.. choice.Cases.Select((c, i) => Arm(cases[i], c.Body, before))];
        arms.Add(Arm(fallback, choice.Default, before));
        Join(arms);
    }

    private (Pending? End, IrVar[] Env) Arm(Pending start, ImmutableArray<Stmt> body, IrVar[] before)
    {
        env = [.. before];
        Enter(start);
        LowerAll(body);
        return (current, [.. env]);
    }

    private void Join(List<(Pending? End, IrVar[] Env)> arms)
    {
        List<(Pending End, IrVar[] Env)> live = [.. arms.Where(static a => a.End is not null).Select(static a => (a.End!, a.Env))];
        if (live.Count == 0)
        {
            current = null;
            return;
        }

        Pending join = NewBlock();
        foreach ((Pending end, _) in live)
        {
            end.Terminator = new IrGoto(join.Id);
        }

        Enter(join);
        env = [.. live[0].Env];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (live.Any(a => a.Env[slot] != live[0].Env[slot]))
            {
                IrVar merged = Version(slot);
                join.Phis.Add((merged, [.. live.Select(a => (a.End.Id, a.Env[slot]))]));
                env[slot] = merged;
            }
        }
    }

    private void LowerLoop(Loop loop)
    {
        IrVar start = Constant(0);
        IrVar limit = Constant((uint)loop.Count);
        IrVar one = Constant(1);
        Pending preheader = current!;
        Pending header = NewBlock();
        Seal(new IrGoto(header.Id));
        Enter(header);
        List<(IrBlockId, IrVar)>[] incoming = new List<(IrBlockId, IrVar)>[SlotCount];
        for (int slot = 0; slot < SlotCount; slot++)
        {
            IrVar merged = Version(slot);
            incoming[slot] = [(preheader.Id, env[slot])];
            header.Phis.Add((merged, incoming[slot]));
            env[slot] = merged;
        }

        IrVar index = new($"i{counter++}", Bv32);
        List<(IrBlockId, IrVar)> indexIncoming = [(preheader.Id, start)];
        header.Phis.Add((index, indexIncoming));
        IrVar more = Temp(Bool);
        Emit(new IrBinary(more, IrBinaryOp.Ult, index, limit));
        Pending body = NewBlock();
        Pending exit = NewBlock();
        Seal(new IrBranch(more, body.Id, exit.Id));
        IrVar[] atHeader = [.. env];

        Enter(body);
        LowerAll(loop.Body);
        if (current is not null)
        {
            IrVar next = Temp(Bv32);
            Emit(new IrBinary(next, IrBinaryOp.Add, index, one));
            Pending latch = current;
            Seal(new IrGoto(header.Id));
            for (int slot = 0; slot < SlotCount; slot++)
            {
                incoming[slot].Add((latch.Id, env[slot]));
            }

            indexIncoming.Add((latch.Id, next));
        }

        Enter(exit);
        env = atHeader;
    }

    private sealed class Pending(IrBlockId id)
    {
        public IrBlockId Id { get; } = id;

        public List<(IrVar Target, List<(IrBlockId, IrVar)> Incoming)> Phis { get; } = [];

        public List<IrInstruction> Body { get; } = [];

        public IrTerminator? Terminator { get; set; }

        public IrBlock Build() =>
            new(Id, [.. Phis.Select(static p => (IrInstruction)new IrPhi(p.Target, [.. p.Incoming])), .. Body], Terminator!);
    }
}
