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
    /// <param name="taint">
    /// Marks the calls whose result and <c>threw</c> flag are abstractions (ADR 0026), which <c>IrPure</c> results will
    /// also be once that instruction exists. Taint then flows through every instruction and phi, a tainted call's event
    /// is tainted, and after a branch or switch on a tainted condition every later definition, trace event, final by-ref
    /// value and the outcome are. <see cref="IrRun.Taint"/> records it; without a predicate it is
    /// <see cref="IrTaint.None"/>. Taint never changes a value.
    /// </param>
    /// <exception cref="ArgumentException">The procedure does not validate, or the inputs do not match its parameters.</exception>
    public static IrRun Run(IrProcedure procedure, IrInputs inputs, ICallOracle oracle, int stepBudget, Func<CallIdentity, bool>? taint = null)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(oracle);
        ArgumentOutOfRangeException.ThrowIfNegative(stepBudget);
        ImmutableArray<IrDiagnostic> diagnostics = IrValidator.Validate(procedure);
        bool typesMatch = inputs.Arguments.Select(static a => a.Type).SequenceEqual(procedure.Parameters.Select(static p => p.Var.Type));
        return diagnostics switch
        {
            { IsEmpty: false } => throw new ArgumentException($"Procedure is not valid IR: {diagnostics[0].Id} {diagnostics[0].Message}", nameof(procedure)),
            _ when !typesMatch => throw new ArgumentException("Inputs do not match the procedure's parameter types.", nameof(inputs)),
            _ => new IrMachine(oracle, taint ?? (static _ => false)).Execute(procedure, inputs, stepBudget),
        };
    }

    private readonly record struct IrJump(IrBlockId? Next, IrOutcome? Outcome, ImmutableArray<IrOut> Outs);

    private sealed class IrMachine(ICallOracle oracle, Func<CallIdentity, bool> abstraction) : IIrInstructionVisitor<IrOutcome?>
    {
        private readonly Dictionary<string, IrValue> values = new(StringComparer.Ordinal);
        private readonly ImmutableArray<IrCallRecord>.Builder trace = ImmutableArray.CreateBuilder<IrCallRecord>();
        private readonly HashSet<string> tainted = new(StringComparer.Ordinal);
        private readonly ImmutableArray<int>.Builder taintedEvents = ImmutableArray.CreateBuilder<int>();
        private readonly ImmutableArray<CallIdentity>.Builder sources = ImmutableArray.CreateBuilder<CallIdentity>();

        /// <summary>The run branched on a tainted condition, so everything it does from here on is tainted.</summary>
        private bool control;

        /// <summary>The instruction being executed reads a tainted value, or is a tainting call.</summary>
        private bool data;

        public IrRun Execute(IrProcedure procedure, IrInputs inputs, int stepBudget)
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
                List<(IrVar Target, IrVar From)> phis =
                    [.. block.Instructions.OfType<IrPhi>().Select(phi => (phi.Target, phi.Incoming.First(i => i.From == previous).Value))];
                List<(IrVar Target, IrValue Value, bool Tainted)> bound = [.. phis.Select(p => (p.Target, Get(p.From), IsTainted(p.From)))];
                foreach ((IrVar target, IrValue value, bool isTainted) in bound)
                {
                    Bind(target, value, isTainted);
                }

                foreach (IrInstruction instruction in block.Instructions)
                {
                    data = instruction.Uses().Any(IsTainted);
                    IrOutcome? stop = steps++ == stepBudget ? new IrBudgetExhausted() : instruction.Accept(this);
                    if (stop is not null)
                    {
                        return Finish(stop, [], returned: null);
                    }
                }

                IrJump jump = steps++ == stepBudget ? new IrJump(Next: null, new IrBudgetExhausted(), []) : block.Terminator.Accept(terminators);
                if (jump.Outcome is not null)
                {
                    return Finish(jump.Outcome, jump.Outs, (block.Terminator as IrReturn)?.Value);
                }

                previous = block.Id;
                block = blocks[jump.Next!];
            }
        }

        public IrOutcome? Visit(IrConst instruction) => Set(instruction.Target, instruction.Value);

        public IrOutcome? Visit(IrBinary instruction) =>
            Set(instruction.Target, IrBits.Binary(instruction.Op, Get(instruction.A), Get(instruction.B)));

        public IrOutcome? Visit(IrOverflows instruction) =>
            Set(
                instruction.Target,
                new IrBoolValue(IrBits.Overflows(instruction.Op, (IrBitVecValue)Get(instruction.A), (IrBitVecValue)Get(instruction.B))));

        public IrOutcome? Visit(IrUnary instruction) =>
            Set(instruction.Target, IrBits.UnaryOp(instruction.Op, Get(instruction.A), instruction.Target.Type));

        /// <summary>Phis were bound on block entry.</summary>
        public IrOutcome? Visit(IrPhi instruction) => null;

        public IrOutcome? Visit(IrCall instruction)
        {
            ImmutableArray<IrValue> args = [.. instruction.Args.Select(Get)];
            ImmutableArray<IrHeapSlice> heap = [.. instruction.Heap.Select(h => new IrHeapSlice(h.Map, Get(h.Before)))];
            int position = trace.Count;
            trace.Add(new IrCallRecord(instruction.Callee, args) { Heap = heap });
            if (abstraction(instruction.Callee))
            {
                data = true;
                if (!sources.Contains(instruction.Callee))
                {
                    sources.Add(instruction.Callee);
                }
            }

            if (data || control)
            {
                taintedEvents.Add(position);
            }
            IrCallResult result = oracle.Answer(instruction.Callee, args, instruction.Target?.Type, position, heap);
            if (instruction.Target is not null)
            {
                Set(instruction.Target, result.Value?.Type == instruction.Target.Type
                    ? result.Value
                    : throw new InvalidOperationException($"Call oracle answered {instruction.Callee.Value} with a value that is not of type {IrText.Type(instruction.Target.Type)}."));
            }

            ImmutableArray<IrValue> after = result.Heap.IsEmpty ? [.. heap.Select(static h => h.Value)] : result.Heap;
            if (!after.Select(static v => v.Type).SequenceEqual(instruction.Heap.Select(static h => h.After.Type)))
            {
                throw new InvalidOperationException($"Call oracle answered {instruction.Callee.Value} with a heap that is not one value per slice it was given, of the slice's type.");
            }

            foreach ((IrHeapPair pair, IrValue value) in instruction.Heap.Zip(after))
            {
                Set(pair.After, value);
            }

            return instruction.Threw is null ? null : Set(instruction.Threw, new IrBoolValue(result.Threw));
        }

        public IrOutcome? Visit(IrMapRead instruction) =>
            Set(instruction.Target, ((IrMapValue)Get(instruction.Map)).Read(Get(instruction.Key)));

        public IrOutcome? Visit(IrMapWrite instruction) =>
            Set(instruction.Target, ((IrMapValue)Get(instruction.Map)).Write(Get(instruction.Key), Get(instruction.Value)));

        public IrOutcome? Visit(IrOpaque instruction) => new IrOpaqueReached(instruction.Reason, instruction.Span);

        private IrValue Get(IrVar var) => values[var.Name];

        private bool IsTainted(IrVar var) => tainted.Contains(var.Name);

        private IrOutcome? Set(IrVar var, IrValue value)
        {
            Bind(var, value, data);
            return null;
        }

        private void Bind(IrVar var, IrValue value, bool isTainted)
        {
            values[var.Name] = value;
            if (isTainted || control)
            {
                tainted.Add(var.Name);
            }
            else
            {
                tainted.Remove(var.Name);
            }
        }

        /// <summary>A branch or switch on a tainted <paramref name="condition"/> taints the rest of the run.</summary>
        private void Branch(IrVar condition) => control |= IsTainted(condition);

        private IrRun Finish(IrOutcome outcome, ImmutableArray<IrOut> outs, IrVar? returned) =>
            new(outcome, ImmutableArray.CreateRange(outs, static (o, values) => values[o.Final.Name], values), trace.ToImmutable())
            {
                Taint = new IrTaint(
                    control,
                    control || (returned is not null && IsTainted(returned)),
                    [.. Enumerable.Range(0, outs.Length).Where(i => control || IsTainted(outs[i].Final))],
                    taintedEvents.ToImmutable(),
                    sources.ToImmutable()),
            };

        private sealed class IrStepper(IrMachine machine) : IIrTerminatorVisitor<IrJump>
        {
            public IrJump Visit(IrGoto terminator) => new(terminator.Target, Outcome: null, []);

            public IrJump Visit(IrBranch terminator)
            {
                machine.Branch(terminator.Cond);
                return new(((IrBoolValue)machine.Get(terminator.Cond)).Value ? terminator.Then : terminator.Else, Outcome: null, []);
            }

            public IrJump Visit(IrSwitch terminator)
            {
                machine.Branch(terminator.Scrutinee);
                IrValue scrutinee = machine.Get(terminator.Scrutinee);
                IrBlockId target = terminator.Cases.Where(c => c.Value == scrutinee).Select(static c => c.Target).FirstOrDefault(terminator.Default);
                return new(target, Outcome: null, []);
            }

            public IrJump Visit(IrReturn terminator) =>
                new(Next: null, new IrReturned(terminator.Value is null ? null : machine.Get(terminator.Value)), terminator.Outs);

            public IrJump Visit(IrThrow terminator) => new(Next: null, new IrThrew(terminator.ExceptionType), terminator.Outs);

            public IrJump Visit(IrUnreachable terminator) => new(Next: null, new IrInfeasible(), []);
        }
    }
}
