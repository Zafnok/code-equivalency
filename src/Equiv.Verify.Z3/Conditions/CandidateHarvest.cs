using System.Collections.Immutable;

using Equiv.Core.Ir;

using SharedParameter = Equiv.Verify.Z3.ProductEncoder.SharedParameter;

namespace Equiv.Verify.Z3.Conditions;

/// <summary>
/// The proposer of ADR 0048 (decision 2; ticket P1-022): the Bool values both bodies of a pair compute from shared inputs
/// alone. A shared input is a source parameter both sides have, such a parameter's <c>null.&lt;Sort&gt;</c> shadow, or a
/// Bool or bitvector constant; a value reaches them through <see cref="IrBinary"/> and <see cref="IrUnary"/> only. Anything
/// else in between (a call, a read of another map, an <see cref="IrPure"/>, a phi, an opaque node, a parameter one side
/// lacks, a truncation, which has no one source spelling) leaves the value out. Each value gives two candidates, itself and
/// its negation. Nothing is synthesised: a candidate is a predicate a body already computes.
/// </summary>
internal static class CandidateHarvest
{
    /// <summary>The most candidates a pair is checked on (the ticket's size guard).</summary>
    public const int MaxCandidates = 16;

    /// <summary>The most source parameters a candidate may read (the ticket's size guard: three or more is a second ticket).</summary>
    public const int MaxInputs = 2;

    /// <summary>
    /// The tallest value harvested. A body's values are a graph that shares operands, and a term is that graph as a tree, so
    /// its size can double with each level; this keeps a term to a few hundred nodes.
    /// </summary>
    public const int MaxDepth = 8;

    private const string NullPrefix = "null.";

    /// <summary>
    /// The candidates of a pair whose product has the inputs <paramref name="shared"/>: the legacy side's values in source
    /// order, then the modern side's, each once, the shallowest first, and at most <see cref="MaxCandidates"/>.
    /// </summary>
    public static ImmutableArray<ConditionTerm> Of(IrProcedure old, IrProcedure @new, ImmutableArray<SharedParameter> shared) =>
    [
        .. Values(old, shared, static s => s.Old)
            .Concat(Values(@new, shared, static s => s.New))
            .Distinct()
            .OrderBy(static v => v.Depth)
            .Take(MaxCandidates / 2)
            .SelectMany(static v => new ConditionTerm[] { v, new ConditionTerm.Not(v) }),
    ];

    /// <summary>One side's values, a negation read as the value it negates.</summary>
    private static IEnumerable<ConditionTerm> Values(IrProcedure procedure, ImmutableArray<SharedParameter> shared, Func<SharedParameter, IrParameter?> side)
    {
        Resolver resolver = new(procedure, shared, side);
        return procedure.Blocks
            .SelectMany(static b => b.Instructions.Select(Target).Append((b.Terminator as IrBranch)?.Cond))
            .OfType<IrVar>()
            .Where(static v => v.Type is IrBool)
            .Select(resolver.Resolve)
            .OfType<ConditionTerm>()
            .Select(Positive)
            .Where(static t => t.Inputs().Distinct().Count() is >= 1 and <= MaxInputs);
    }

    private static IrVar? Target(IrInstruction instruction) => instruction switch
    {
        IrBinary binary => binary.Target,
        IrUnary unary => unary.Target,
        IrMapRead read => read.Target,
        _ => null,
    };

    private static ConditionTerm Positive(ConditionTerm term) => term is ConditionTerm.Not not ? Positive(not.A) : term;

    /// <summary>One side's variables as terms over the shared inputs, each resolved once.</summary>
    private sealed class Resolver
    {
        private readonly Dictionary<string, (int Index, IrType Type)> inputs;
        private readonly Dictionary<string, IrInstruction> definitions;
        private readonly Dictionary<string, ConditionTerm?> resolved = new(StringComparer.Ordinal);

        public Resolver(IrProcedure procedure, ImmutableArray<SharedParameter> shared, Func<SharedParameter, IrParameter?> side)
        {
            inputs = shared
                .Select(static (s, i) => (Shared: s, Index: i))
                .Where(static s => s.Shared is { Old: not null, New: not null })
                .ToDictionary(s => side(s.Shared)!.Var.Name, static s => (s.Index, s.Shared.Type), StringComparer.Ordinal);
            definitions = procedure.Blocks
                .SelectMany(static b => b.Instructions)
                .Select(static i => (Target: i is IrConst constant ? constant.Target : Target(i), Instruction: i))
                .Where(static d => d.Target is not null)
                .ToDictionary(static d => d.Target!.Name, static d => d.Instruction, StringComparer.Ordinal);
        }

        public ConditionTerm? Resolve(IrVar variable)
        {
            if (!resolved.TryGetValue(variable.Name, out ConditionTerm? term))
            {
                term = Compute(variable) is { Depth: <= MaxDepth } computed ? computed : null;
                resolved.Add(variable.Name, term);
            }

            return term;
        }

        /// <summary>A variable nothing harvestable defines is a term only when it is a source parameter both sides have.</summary>
        private ConditionTerm? Compute(IrVar variable) => definitions.GetValueOrDefault(variable.Name) switch
        {
            null when inputs.TryGetValue(variable.Name, out (int Index, IrType Type) input) && !IrParameterNames.IsSynthesised(variable.Name) => new ConditionTerm.Input(input.Index, input.Type),
            IrConst { Value: IrBoolValue or IrBitVecValue } constant => new ConditionTerm.Constant(constant.Value),
            IrBinary binary when Resolve(binary.A) is { } a && Resolve(binary.B) is { } b => new ConditionTerm.Binary(binary.Op, a, b),
            IrUnary { Op: IrUnaryOp.BoolNot } unary when Resolve(unary.A) is { } a => new ConditionTerm.Not(a),
            IrUnary { Op: not (IrUnaryOp.BoolNot or IrUnaryOp.Trunc) } unary when Resolve(unary.A) is { } a => new ConditionTerm.Unary(unary.Op, a, unary.Target.Type),
            IrMapRead read when IsNullShadow(read, out int map) && Resolve(read.Key) is ConditionTerm.Input reference => new ConditionTerm.Null(map, reference),
            _ => null,
        };

        /// <summary>Whether <paramref name="read"/> reads a <c>null.&lt;Sort&gt;</c> input both sides have, at index <paramref name="map"/>.</summary>
        private bool IsNullShadow(IrMapRead read, out int map)
        {
            bool isInput = inputs.TryGetValue(read.Map.Name, out (int Index, IrType Type) input);
            map = input.Index;
            return isInput && read.Map.Name.StartsWith(NullPrefix, StringComparison.Ordinal);
        }
    }
}
