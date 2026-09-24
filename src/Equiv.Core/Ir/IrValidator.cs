using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Equiv.Core.Ir;

/// <summary>Checks the well-formedness rules of ticket M1-002 (SSA, dominance, phis, types, targets, outs).</summary>
public static class IrValidator
{
    private static readonly FrozenSet<IrBinaryOp> Comparisons = new[]
    {
        IrBinaryOp.Eq, IrBinaryOp.Ne, IrBinaryOp.Slt, IrBinaryOp.Sle, IrBinaryOp.Sgt, IrBinaryOp.Sge,
        IrBinaryOp.Ult, IrBinaryOp.Ule, IrBinaryOp.Ugt, IrBinaryOp.Uge,
    }.ToFrozenSet();

    private static readonly FrozenSet<IrBinaryOp> BoolOps =
        new[] { IrBinaryOp.And, IrBinaryOp.Or, IrBinaryOp.Xor, IrBinaryOp.Eq, IrBinaryOp.Ne }.ToFrozenSet();

    /// <summary>Sign of (target width - operand width) each bitvector unary operation requires.</summary>
    private static readonly FrozenDictionary<IrUnaryOp, int> WidthChange = new Dictionary<IrUnaryOp, int>
    {
        [IrUnaryOp.Neg] = 0,
        [IrUnaryOp.Not] = 0,
        [IrUnaryOp.ZExt] = 1,
        [IrUnaryOp.SExt] = 1,
        [IrUnaryOp.Trunc] = -1,
    }.ToFrozenDictionary();

    public static ImmutableArray<IrDiagnostic> Validate(IrProcedure procedure)
    {
        ArgumentNullException.ThrowIfNull(procedure);
        return new IrChecker(procedure).Run();
    }

    private sealed record IrDefinition(IrVar Var, IrBlockId? Block, int Index);

    private sealed class IrChecker(IrProcedure procedure)
    {
        private readonly ImmutableArray<IrDiagnostic>.Builder diagnostics = ImmutableArray.CreateBuilder<IrDiagnostic>();
        private readonly Dictionary<IrBlockId, IrBlock> blocks = [];
        private readonly Dictionary<IrBlockId, HashSet<IrBlockId>> predecessors = [];
        private readonly Dictionary<string, IrDefinition> definitions = new(StringComparer.Ordinal);
        private readonly Dictionary<IrBlockId, int> reversePostorder = [];
        private readonly Dictionary<IrBlockId, IrBlockId> immediateDominator = [];

        public ImmutableArray<IrDiagnostic> Run()
        {
            IndexBlocks();
            CheckTargets();
            CollectDefinitions();
            CheckPhis();
            CheckTypes();
            CheckOuts();
            if (blocks.ContainsKey(procedure.Entry))
            {
                ComputeDominators();
            }
            else
            {
                Report(IrDiagnosticIds.MissingEntry, block: null, $"entry block {IrText.Block(procedure.Entry)} does not exist");
            }

            CheckUses();
            return diagnostics.ToImmutable();
        }

        private void Report(string id, IrBlockId? block, string message) => diagnostics.Add(new IrDiagnostic(id, block, message));

        private void IndexBlocks()
        {
            foreach (IrBlock block in procedure.Blocks)
            {
                if (!blocks.TryAdd(block.Id, block))
                {
                    Report(IrDiagnosticIds.DuplicateBlockId, block.Id, $"block id {IrText.Block(block.Id)} is used more than once");
                }

                predecessors.TryAdd(block.Id, []);
            }
        }

        private void CheckTargets()
        {
            foreach (IrBlock block in procedure.Blocks)
            {
                foreach (IrBlockId target in block.Terminator.Successors())
                {
                    if (predecessors.TryGetValue(target, out HashSet<IrBlockId>? into))
                    {
                        into.Add(block.Id);
                    }
                    else
                    {
                        Report(IrDiagnosticIds.MissingTarget, block.Id, $"target {IrText.Block(target)} does not exist");
                    }
                }
            }
        }

        private void CollectDefinitions()
        {
            foreach (IrParameter parameter in procedure.Parameters)
            {
                Define(parameter.Var, block: null, -1);
            }

            foreach (IrBlock block in procedure.Blocks)
            {
                for (int i = 0; i < block.Instructions.Length; i++)
                {
                    foreach (IrVar var in block.Instructions[i].Definitions())
                    {
                        Define(var, block.Id, i);
                    }
                }
            }
        }

        private void Define(IrVar var, IrBlockId? block, int index)
        {
            if (!definitions.TryAdd(var.Name, new IrDefinition(var, block, index)))
            {
                Report(IrDiagnosticIds.MultipleAssignment, block, $"%{var.Name} is assigned more than once");
            }
        }

        private void CheckPhis()
        {
            foreach (IrBlock block in procedure.Blocks)
            {
                bool pastPhis = false;
                foreach (IrInstruction instruction in block.Instructions)
                {
                    if (instruction is not IrPhi phi)
                    {
                        pastPhis = true;
                        continue;
                    }

                    if (pastPhis | (block.Id == procedure.Entry))
                    {
                        Report(IrDiagnosticIds.PhiPlacement, block.Id, $"phi must start a non-entry block: {IrText.Line(phi)}");
                    }

                    List<IrBlockId> from = [.. phi.Incoming.Select(static i => i.From)];
                    if ((from.Distinct().Take(from.Count + 1).Count() != from.Count) | !predecessors[block.Id].SetEquals(from))
                    {
                        Report(IrDiagnosticIds.PhiPredecessors, block.Id, $"phi incoming blocks are not the predecessors: {IrText.Line(phi)}");
                    }
                }
            }
        }

        private void CheckTypes()
        {
            IrTypeChecker checker = new(procedure.ReturnType);
            foreach (IrBlock block in procedure.Blocks)
            {
                foreach (IrInstruction instruction in block.Instructions)
                {
                    ReportType(block.Id, instruction.Accept(checker), IrText.Line(instruction));
                }

                ReportType(block.Id, block.Terminator.Accept(checker.Terminators), IrText.Line(block.Terminator));
            }
        }

        private void ReportType(IrBlockId block, string? id, string line)
        {
            if (id is not null)
            {
                Report(id, block, $"types do not fit: {line}");
            }
        }

        private void CheckOuts()
        {
            ImmutableArray<IrVar> byRef = [.. procedure.Parameters.Where(static p => p.Kind != IrParameterKind.In).Select(static p => p.Var)];
            foreach (IrBlock block in procedure.Blocks)
            {
                ImmutableArray<IrOut>? outs = block.Terminator switch
                {
                    IrReturn exit => exit.Outs,
                    IrThrow exit => exit.Outs,
                    _ => null,
                };
                if (outs is { } exitOuts
                    && !(exitOuts.Select(static o => o.Param).SequenceEqual(byRef) & exitOuts.All(static o => o.Final.Type == o.Param.Type)))
                {
                    Report(IrDiagnosticIds.ExitOuts, block.Id, $"outs must name every by-ref parameter once, in order: {IrText.Line(block.Terminator)}");
                }
            }
        }

        /// <summary>Cooper, Harvey and Kennedy, "A Simple, Fast Dominance Algorithm" (2001).</summary>
        private void ComputeDominators()
        {
            List<IrBlockId> postorder = [];
            Walk(procedure.Entry, [], postorder);
            postorder.Reverse();
            for (int i = 0; i < postorder.Count; i++)
            {
                reversePostorder[postorder[i]] = i;
            }

            immediateDominator[procedure.Entry] = procedure.Entry;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (IrBlockId block in postorder.Skip(1))
                {
                    IrBlockId newDominator = predecessors[block]
                        .Where(immediateDominator.ContainsKey)
                        .Aggregate(Intersect);
                    changed |= !immediateDominator.TryGetValue(block, out IrBlockId? old) || old != newDominator;
                    immediateDominator[block] = newDominator;
                }
            }
        }

        private void Walk(IrBlockId block, HashSet<IrBlockId> visited, List<IrBlockId> postorder)
        {
            visited.Add(block);
            foreach (IrBlockId successor in blocks[block].Terminator.Successors().Where(successor => blocks.ContainsKey(successor) && !visited.Contains(successor)))
            {
                Walk(successor, visited, postorder);
            }

            postorder.Add(block);
        }

        private IrBlockId Intersect(IrBlockId left, IrBlockId right)
        {
            while (left != right)
            {
                while (reversePostorder[left] > reversePostorder[right])
                {
                    left = immediateDominator[left];
                }

                while (reversePostorder[right] > reversePostorder[left])
                {
                    right = immediateDominator[right];
                }
            }

            return left;
        }

        private bool Dominates(IrBlockId dominator, IrBlockId block)
        {
            while (block != dominator && block != procedure.Entry)
            {
                block = immediateDominator[block];
            }

            return block == dominator;
        }

        private void CheckUses()
        {
            foreach (IrBlock block in procedure.Blocks)
            {
                for (int i = 0; i < block.Instructions.Length; i++)
                {
                    IrInstruction instruction = block.Instructions[i];
                    foreach (IrVar use in instruction.Uses())
                    {
                        CheckUse(use, block.Id, i);
                    }

                    foreach ((IrBlockId from, IrVar value) in (instruction as IrPhi)?.Incoming ?? [])
                    {
                        CheckUse(value, from, int.MaxValue);
                    }
                }

                foreach (IrVar use in block.Terminator.Uses())
                {
                    CheckUse(use, block.Id, block.Instructions.Length);
                }
            }
        }

        /// <summary>
        /// A use at (<paramref name="block"/>, <paramref name="index"/>) needs a matching definition that
        /// dominates it. Parameters dominate everything; code unreachable from the entry only needs a definition.
        /// </summary>
        private void CheckUse(IrVar use, IrBlockId block, int index)
        {
            if (!definitions.TryGetValue(use.Name, out IrDefinition? definition) || definition.Var != use)
            {
                Report(IrDiagnosticIds.UseNotDominated, block, $"%{use.Name} is undefined or differs from its definition");
                return;
            }

            if (definition.Block is not { } defined || !reversePostorder.ContainsKey(block))
            {
                return;
            }

            if (defined == block ? definition.Index >= index : !Dominates(defined, block))
            {
                Report(IrDiagnosticIds.UseNotDominated, block, $"%{use.Name} is used where its definition does not dominate");
            }
        }
    }

    /// <summary>Returns the violated rule id (<see cref="IrDiagnosticIds.OperandTypes"/> or <see cref="IrDiagnosticIds.MapTypes"/>), or null.</summary>
    private sealed class IrTypeChecker(IrType? returnType) : IIrInstructionVisitor<string?>
    {
        public IrTerminatorTypeChecker Terminators { get; } = new(returnType);

        public string? Visit(IrConst instruction) => Operands(instruction.Value.Type == instruction.Target.Type);

        public string? Visit(IrBinary instruction)
        {
            IrType operand = instruction.A.Type;
            IrType? result = operand switch
            {
                IrBitVec when Comparisons.Contains(instruction.Op) => new IrBool(),
                IrBitVec => operand,
                IrBool when BoolOps.Contains(instruction.Op) => operand,
                IrSort when instruction.Op is IrBinaryOp.Eq or IrBinaryOp.Ne => new IrBool(),
                _ => null,
            };
            return Operands((operand == instruction.B.Type) & (result == instruction.Target.Type));
        }

        public string? Visit(IrOverflows instruction) =>
            Operands((instruction.A.Type is IrBitVec) & (instruction.A.Type == instruction.B.Type) & (instruction.Target.Type is IrBool));

        public string? Visit(IrUnary instruction)
        {
            return instruction.Op == IrUnaryOp.BoolNot
                ? Operands((instruction.A.Type is IrBool) && (instruction.Target.Type is IrBool))
                : Operands(instruction.A.Type is IrBitVec operand
                && instruction.Target.Type is IrBitVec target
                && Math.Sign(target.Width - operand.Width) == WidthChange[instruction.Op]);
        }

        public string? Visit(IrPhi instruction) => Operands(instruction.Incoming.All(i => i.Value.Type == instruction.Target.Type));

        public string? Visit(IrCall instruction) => Operands(instruction.Threw is null || instruction.Threw.Type is IrBool);

        public string? Visit(IrMapRead instruction) =>
            Maps(instruction.Map.Type is IrMap map && (map.Key == instruction.Key.Type) & (map.Value == instruction.Target.Type));

        public string? Visit(IrMapWrite instruction) =>
            Maps(instruction.Map.Type is IrMap map
                && (map.Key == instruction.Key.Type) & (map.Value == instruction.Value.Type) & (map == instruction.Target.Type));

        public string? Visit(IrOpaque instruction) => null;

        internal static string? Operands(bool ok) => ok ? null : IrDiagnosticIds.OperandTypes;

        private static string? Maps(bool ok) => ok ? null : IrDiagnosticIds.MapTypes;
    }

    private sealed class IrTerminatorTypeChecker(IrType? returnType) : IIrTerminatorVisitor<string?>
    {
        public string? Visit(IrGoto terminator) => null;

        public string? Visit(IrBranch terminator) => IrTypeChecker.Operands(terminator.Cond.Type is IrBool);

        public string? Visit(IrSwitch terminator) =>
            IrTypeChecker.Operands(terminator.Cases.All(c => c.Value.Type == terminator.Scrutinee.Type));

        public string? Visit(IrReturn terminator) => IrTypeChecker.Operands(terminator.Value?.Type == returnType);

        public string? Visit(IrThrow terminator) => null;

        public string? Visit(IrUnreachable terminator) => null;
    }
}
