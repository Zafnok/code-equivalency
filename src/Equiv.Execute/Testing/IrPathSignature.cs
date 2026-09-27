using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Core.Ir;

namespace Equiv.Execute.Testing;

/// <summary>
/// The IR path part of a species (ticket P1-008): the ids of the blocks <see cref="IrInterpreter"/> enters on one side's
/// body for an input, up to the first opaque node or the first branch on an abstraction, where every call and pure
/// function is one (<see cref="IrRun.Path"/>). The input's wire arguments become the body's C# parameters by position: a
/// <c>bool</c> as itself, an integer, <c>char</c> or enum as its bitvector, and any other value as a sort element, one per
/// distinct wire text, which the <c>null.&lt;Sort&gt;</c> map holds when the text is <c>null</c>. The receiver is one more
/// element, and every other synthesised input (a heap map, a cast or type-test map) and every missing argument is its
/// type's default. No user assembly is instrumented.
/// </summary>
internal static class IrPathSignature
{
    private const int StepBudget = 1_000;

    private const string NullPrefix = "null.";

    private const string Null = "null";

    public static string Of(IrProcedure body, ExecutionInput input)
    {
        IrRun run = IrInterpreter.Run(body, Inputs(body, input), DefaultOracle.Instance, StepBudget, static _ => true, DefaultOracle.Instance);
        return string.Join(',', run.Path.Select(static b => b.Value.ToString(CultureInfo.InvariantCulture)));
    }

    public static IrInputs Inputs(IrProcedure body, ExecutionInput input)
    {
        Dictionary<(string Sort, string Text), IrSortValue> elements = [];
        Dictionary<string, IrValue> values = new(StringComparer.Ordinal);
        IrParameter[] positional = [.. body.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name))];
        foreach ((IrParameter parameter, string argument) in positional.Zip(input.Arguments))
        {
            values[parameter.Var.Name] = Decode(parameter.Var.Type, argument, elements);
        }

        return new IrInputs([.. body.Parameters.Select(p => values.GetValueOrDefault(p.Var.Name) ?? Synthesised(p.Var, elements))]);
    }

    private static IrValue Decode(IrType type, string argument, Dictionary<(string Sort, string Text), IrSortValue> elements) => type switch
    {
        IrBool => new IrBoolValue(string.Equals(argument, "true", StringComparison.Ordinal)),
        IrBitVec bitVec => Integer(bitVec.Width, argument),
        IrSort sort => Element(sort.Name, argument, elements),
        _ => DefaultOracle.Default(type),
    };

    /// <summary>A decimal integer as <paramref name="width"/> bits: two's complement when negative; zero when it is no integer.</summary>
    private static IrBitVecValue Integer(int width, string argument) =>
        long.TryParse(argument, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long signed) ? IrBitVecValue.FromSigned(width, signed)
        : ulong.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out ulong bits) ? IrBitVecValue.FromSigned(width, unchecked((long)bits))
        : new IrBitVecValue(width, 0);

    /// <summary>The element of <paramref name="sort"/> for <paramref name="text"/>, numbered from 1 in order of first use.</summary>
    private static IrSortValue Element(string sort, string text, Dictionary<(string Sort, string Text), IrSortValue> elements)
    {
        if (!elements.TryGetValue((sort, text), out IrSortValue? element))
        {
            element = new IrSortValue(sort, elements.Count + 1);
            elements[(sort, text)] = element;
        }

        return element;
    }

    private static IrValue Synthesised(IrVar input, Dictionary<(string Sort, string Text), IrSortValue> elements) => input switch
    {
        { Name: IrParameterNames.Receiver, Type: IrSort sort } => Element(sort.Name, IrParameterNames.Receiver, elements),
        { Type: IrMap { Value: IrBool } map } when input.Name.StartsWith(NullPrefix, StringComparison.Ordinal) =>
            elements.Where(e => string.Equals(e.Key.Text, Null, StringComparison.Ordinal) && map.Key == e.Value.Type)
                .Aggregate((IrMapValue)DefaultOracle.Default(map), static (nulls, e) => nulls.Write(e.Value, new IrBoolValue(Value: true))),
        _ => DefaultOracle.Default(input.Type),
    };
}
