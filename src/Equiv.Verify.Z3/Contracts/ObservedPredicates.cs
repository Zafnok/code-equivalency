using System.Collections.Immutable;

using Equiv.Core.Ir;

namespace Equiv.Verify.Z3.Contracts;

/// <summary>
/// The first candidate of ticket P1-010 (ADR 0036 decision 2): every predicate a caller applies to a call's result or to a
/// heap map the call wrote. Each Bool the caller defines by a comparison, a Bool operation or a map read is followed back
/// through constants, operations and map reads; it is a predicate of the call when every variable it reaches is one of
/// that call's outputs, a constant, or a synthesised input the caller never changes, and it reaches at least one output.
/// A Bool result is itself a predicate.
/// </summary>
internal static class ObservedPredicates
{
    /// <summary>What <c>properties.contractsUsed</c> names as the proposer of a candidate built from observed predicates.</summary>
    public const string Name = "observed-predicates";

    /// <summary>The predicates <paramref name="caller"/> applies to the outputs of each call <paramref name="isCallee"/> selects.</summary>
    public static IEnumerable<ObservedPredicate> Of(IrProcedure caller, Func<IrCall, bool> isCallee)
    {
        IrInstruction[] instructions = [.. caller.Blocks.SelectMany(static b => b.Instructions)];
        Dictionary<string, IrInstruction> definitions = new(StringComparer.Ordinal);
        foreach (IrInstruction instruction in instructions)
        {
            if (Target(instruction) is { } target)
            {
                definitions[target.Name] = instruction;
            }
        }

        Dictionary<string, IrParameter> inputs = caller.Parameters
            .Where(static p => p.Kind == IrParameterKind.In && IrParameterNames.IsSynthesised(p.Var.Name))
            .ToDictionary(static p => p.Var.Name, StringComparer.Ordinal);
        IrVar[] bools = [.. instructions.Select(Target).OfType<IrVar>().Where(static v => v.Type is IrBool)];
        foreach (IrCall call in instructions.OfType<IrCall>().Where(isCallee))
        {
            Dictionary<string, PredicateLeaf> outputs = call.Heap.ToDictionary(static h => h.After.Name, static h => new PredicateLeaf(PredicateLeafKind.Heap, h.Map, h.After.Type), StringComparer.Ordinal);
            if (call.Target is { } result)
            {
                outputs[result.Name] = new PredicateLeaf(PredicateLeafKind.Result, string.Empty, result.Type);
            }

            foreach (IrVar candidate in bools)
            {
                Dictionary<string, PredicateLeaf> leaves = new(StringComparer.Ordinal);
                List<IrInstruction> slice = [];
                if (Follow(candidate, outputs, inputs, definitions, leaves, slice) && leaves.Values.Any(static l => l.Kind != PredicateLeafKind.Input))
                {
                    yield return new ObservedPredicate([.. slice], candidate, leaves.ToImmutableDictionary(StringComparer.Ordinal));
                }
            }
        }
    }

    /// <summary>
    /// Adds what computes <paramref name="var"/> to <paramref name="slice"/>, after what it reads, and its leaves to
    /// <paramref name="leaves"/>; false when it reaches anything but an output, a constant or an unchanging input.
    /// </summary>
    private static bool Follow(
        IrVar var,
        Dictionary<string, PredicateLeaf> outputs,
        Dictionary<string, IrParameter> inputs,
        Dictionary<string, IrInstruction> definitions,
        Dictionary<string, PredicateLeaf> leaves,
        List<IrInstruction> slice)
    {
        if (outputs.TryGetValue(var.Name, out PredicateLeaf? output))
        {
            leaves[var.Name] = output;
            return true;
        }

        if (inputs.ContainsKey(var.Name))
        {
            leaves[var.Name] = new PredicateLeaf(PredicateLeafKind.Input, var.Name, var.Type);
            return true;
        }

        if (!definitions.TryGetValue(var.Name, out IrInstruction? definition))
        {
            return false;
        }

        bool followed = definition switch
        {
            IrConst => true,
            IrBinary binary => Follow(binary.A, outputs, inputs, definitions, leaves, slice) && Follow(binary.B, outputs, inputs, definitions, leaves, slice),
            IrUnary unary => Follow(unary.A, outputs, inputs, definitions, leaves, slice),
            IrMapRead read => Follow(read.Map, outputs, inputs, definitions, leaves, slice) && Follow(read.Key, outputs, inputs, definitions, leaves, slice),
            _ => false,
        };
        if (followed && !slice.Contains(definition))
        {
            slice.Add(definition);
        }

        return followed;
    }

    private static IrVar? Target(IrInstruction instruction) => instruction switch
    {
        IrConst constant => constant.Target,
        IrBinary binary => binary.Target,
        IrUnary unary => unary.Target,
        IrMapRead read => read.Target,
        IrCall call => call.Target,
        _ => null,
    };
}
