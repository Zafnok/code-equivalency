using System.Collections.Immutable;
using System.Linq;

using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// Roslyn's control-flow graph has no switch. A <c>switch</c> statement becomes a chain of equality
/// branches on one captured scrutinee, and a <c>switch</c> expression a chain of <c>IsPattern</c>
/// branches on constant patterns. <see cref="Find"/> recognises such a chain so that ticket M2-004
/// acceptance criterion 2 can fold it back into one <see cref="IrSwitch"/>: the chain's head keeps its
/// own instructions and switches, and the test blocks after it are absorbed. A chain needs at least
/// two tests, so a plain <c>if (x == 1)</c> stays a branch, and every case value must be a constant of
/// the scrutinee's own bitvector or Bool type.
/// </summary>
internal sealed class SwitchChains
{
    private readonly Dictionary<int, Chain> heads = [];
    private readonly HashSet<int> absorbed = [];

    private SwitchChains()
    {
    }

    public static SwitchChains Find(ControlFlowGraph cfg)
    {
        SwitchChains chains = new();
        foreach (BasicBlock block in cfg.Blocks.Where(static b => b.IsReachable))
        {
            chains.Grow(block);
        }

        return chains;
    }

    /// <summary>The chain <paramref name="ordinal"/> heads, or null when that block is not one.</summary>
    public Chain? Head(int ordinal) => heads.GetValueOrDefault(ordinal);

    /// <summary>Whether a chain's head has taken over this block's test, so it is not lowered at all.</summary>
    public bool IsAbsorbed(int ordinal) => absorbed.Contains(ordinal);

    private static Test? Read(BasicBlock block)
    {
        if (block.ConditionKind != ControlFlowConditionKind.WhenFalse
            || block.FallThroughSuccessor is not { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } match
            || block.ConditionalSuccessor is not { Semantics: ControlFlowBranchSemantics.Regular, Destination: not null } otherwise)
        {
            return null;
        }

        (IOperation? scrutinee, IOperation? value) = block.BranchValue switch
        {
            IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } equality => (equality.LeftOperand, equality.RightOperand),
            IIsPatternOperation { Pattern: IConstantPatternOperation pattern } test => (test.Value, pattern.Value),
            _ => (null, null),
        };
        return scrutinee is { Type: { } scrutineeType }
            && Key(scrutinee) is { } key
            && TypeMapper.Map(scrutineeType) is IrBitVec or IrBool
            && value is { Type: { } valueType, ConstantValue: { HasValue: true, Value: { } constant } }
            && TypeMapper.Map(valueType) == TypeMapper.Map(scrutineeType)
                ? new Test(key, scrutinee, constant, valueType, match, otherwise)
                : null;
    }

    /// <summary>What makes two tests read the same scrutinee. Anything else cannot be a case test.</summary>
    private static object? Key(IOperation scrutinee) => scrutinee switch
    {
        IFlowCaptureReferenceOperation capture => capture.Id,
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };

    private void Grow(BasicBlock block)
    {
        if (absorbed.Contains(block.Ordinal) || Read(block) is not { } head)
        {
            return;
        }

        List<Test> tests = [head];
        List<int> steps = [];
        // Only a block with nothing of its own and no other way in can be absorbed: its test is the
        // whole block, so moving it into the head changes nothing that any other edge could observe.
        for (BasicBlock next = head.Otherwise.Destination!;
            next.Operations.IsEmpty && next.Predecessors.Length == 1 && Read(next) is { } step && step.ScrutineeKey.Equals(head.ScrutineeKey);
            next = step.Otherwise.Destination!)
        {
            tests.Add(step);
            steps.Add(next.Ordinal);
        }

        if (tests.Count < 2 || tests.Select(static t => t.Constant).Distinct().Take(tests.Count + 1).Count() != tests.Count)
        {
            return;
        }

        heads[block.Ordinal] = new Chain(
            head.Scrutinee,
            [.. tests.Select(static t => (t.Constant, t.ConstantType, t.Match))],
            tests[^1].Otherwise);
        absorbed.UnionWith(steps);
    }

    /// <summary>
    /// A folded chain: the scrutinee to lower in the head block, its cases in order, and the fall-out
    /// edge. Each target is the CFG <em>branch</em>, not the block, so that the lowering runs whatever
    /// <c>finally</c> that edge leaves, exactly as it does for a branch it did not fold.
    /// </summary>
    internal sealed record Chain(
        IOperation Scrutinee,
        ImmutableArray<(object Constant, ITypeSymbol ConstantType, ControlFlowBranch Target)> Cases,
        ControlFlowBranch Default);

    private sealed record Test(object ScrutineeKey, IOperation Scrutinee, object Constant, ITypeSymbol ConstantType, ControlFlowBranch Match, ControlFlowBranch Otherwise);
}
