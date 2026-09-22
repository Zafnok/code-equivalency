using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>
/// Executes a valid procedure on concrete inputs. It is the test oracle for lowering and the
/// replay engine for solver counterexamples (M3-001), so its semantics must match the Z3
/// encoding exactly: bitvector ops wrap, and phis read their values in parallel on block entry.
/// </summary>
public static class IrInterpreter
{
    /// <summary>
    /// Runs <paramref name="procedure"/>. Each executed instruction or terminator costs one step;
    /// when <paramref name="stepBudget"/> steps are spent the run ends with <see cref="IrBudgetExhausted"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The procedure does not validate, or the inputs do not match its parameters.</exception>
    public static IrRun Run(IrProcedure procedure, IrInputs inputs, ICallOracle oracle, int stepBudget)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentOutOfRangeException.ThrowIfNegative(stepBudget);
        ImmutableArray<IrDiagnostic> diagnostics = IrValidator.Validate(procedure);
        return !diagnostics.IsEmpty
            ? throw new ArgumentException($"Procedure is not valid IR: {diagnostics[0].Id} {diagnostics[0].Message}", nameof(procedure))
            : !inputs.Arguments.Select(static a => a.Type).SequenceEqual(procedure.Parameters.Select(static p => p.Var.Type))
                ? throw new ArgumentException("Inputs do not match the procedure's parameter types.", nameof(inputs))
                : new IrMachine(oracle).Run(procedure, inputs, stepBudget);
    }

    private readonly record struct IrJump(IrBlockId? Next, IrOutcome? Outcome, ImmutableArray<IrOut> Outs);

    private sealed class IrMachine(ICallOracle oracle) : IrInstructionVisitor<IrOutcome?>
    {
        private readonly Dictionary<string, IrValue> values = new(StringComparer.Ordinal);
        private readonly ImmutableArray<IrCallRecord>.Builder trace = ImmutableArray.CreateBuilder<IrCallRecord>();

        public IrRun Run(IrProcedure procedure, IrInputs inputs, int stepBudget)
        {
            Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
            for (int i = 0; i < inputs.Arguments.Length; i++)
            {
                values[procedure.Parameters[i].Var.Name] = inputs.Arguments[i];
            }

            IrStepper terminators = new(this);
            IrBlockId? previous = null;
            IrBlock block = blocks[procedure.Entry];
            int steps = 0;
            while (true)
            {
                List<(IrVar Target, IrValue Value)> phis =
                    [.. block.Instructions.OfType<IrPhi>().Select(phi => (phi.Target, Get(phi.Incoming.First(i => i.From == previous).Value)))];
                foreach ((IrVar target, IrValue value) in phis)
                {
                    values[target.Name] = value;
                }

                foreach (IrInstruction instruction in block.Instructions)
                {
                    IrOutcome? stop = steps++ == stepBudget ? new IrBudgetExhausted() : instruction.Accept(this);
                    if (stop is not null)
                    {
                        return new IrRun(stop, [], trace.ToImmutable());
                    }
                }

                IrJump jump = steps++ == stepBudget ? new IrJump(Next: null, new IrBudgetExhausted(), []) : block.Terminator.Accept(terminators);
                if (jump.Outcome is not null)
                {
                    return new IrRun(jump.Outcome, ImmutableArray.CreateRange(jump.Outs, static (o, values) => values[o.Final.Name], values), trace.ToImmutable());
                }

                previous = block.Id;
                block = blocks[jump.Next!];
            }
        }

        public override IrOutcome? Visit(IrConst instruction) => Set(instruction.Target, instruction.Value);

        public override IrOutcome? Visit(IrBinary instruction) =>
            Set(instruction.Target, IrBits.Binary(instruction.Op, Get(instruction.A), Get(instruction.B)));

        public override IrOutcome? Visit(IrOverflows instruction) =>
            Set(
                instruction.Target,
                new IrBoolValue(IrBits.Overflows(instruction.Op, (IrBitVecValue)Get(instruction.A), (IrBitVecValue)Get(instruction.B))));

        public override IrOutcome? Visit(IrUnary instruction) =>
            Set(instruction.Target, IrBits.UnaryOp(instruction.Op, Get(instruction.A), instruction.Target.Type));

        /// <summary>Phis were bound on block entry.</summary>
        public override IrOutcome? Visit(IrPhi instruction) => null;

        public override IrOutcome? Visit(IrCall instruction)
        {
            ImmutableArray<IrValue> args = [.. instruction.Args.Select(Get)];
            int position = trace.Count;
            trace.Add(new IrCallRecord(instruction.Callee, args));
            IrCallResult result = oracle.Answer(instruction.Callee, args, instruction.Target?.Type, position);
            if (instruction.Target is not null)
            {
                Set(instruction.Target, result.Value?.Type == instruction.Target.Type
                    ? result.Value
                    : throw new InvalidOperationException($"Call oracle answered {instruction.Callee.Value} with a value that is not of type {IrText.Type(instruction.Target.Type)}."));
            }

            return instruction.Threw is null ? null : Set(instruction.Threw, new IrBoolValue(result.Threw));
        }

        public override IrOutcome? Visit(IrMapRead instruction) =>
            Set(instruction.Target, ((IrMapValue)Get(instruction.Map)).Read(Get(instruction.Key)));

        public override IrOutcome? Visit(IrMapWrite instruction) =>
            Set(instruction.Target, ((IrMapValue)Get(instruction.Map)).Write(Get(instruction.Key), Get(instruction.Value)));

        public override IrOutcome? Visit(IrOpaque instruction) => new IrOpaqueReached(instruction.Reason, instruction.Span);

        private IrValue Get(IrVar var) => values[var.Name];

        private IrOutcome? Set(IrVar var, IrValue value)
        {
            values[var.Name] = value;
            return null;
        }

        private sealed class IrStepper(IrMachine machine) : IrTerminatorVisitor<IrJump>
        {
            public override IrJump Visit(IrGoto terminator) => new(terminator.Target, Outcome: null, []);

            public override IrJump Visit(IrBranch terminator) =>
                new(((IrBoolValue)machine.Get(terminator.Cond)).Value ? terminator.Then : terminator.Else, Outcome: null, []);

            public override IrJump Visit(IrSwitch terminator)
            {
                IrValue scrutinee = machine.Get(terminator.Scrutinee);
                IrBlockId target = terminator.Cases.Where(c => c.Value == scrutinee).Select(static c => c.Target).FirstOrDefault(terminator.Default);
                return new(target, Outcome: null, []);
            }

            public override IrJump Visit(IrReturn terminator) =>
                new(Next: null, new IrReturned(terminator.Value is null ? null : machine.Get(terminator.Value)), terminator.Outs);

            public override IrJump Visit(IrThrow terminator) => new(Next: null, new IrThrew(terminator.ExceptionType), terminator.Outs);

            public override IrJump Visit(IrUnreachable terminator) => new(Next: null, new IrInfeasible(), []);
        }
    }
}
