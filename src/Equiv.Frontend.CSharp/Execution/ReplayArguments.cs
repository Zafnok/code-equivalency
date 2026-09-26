using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Turns a Divergent's model into one driver case per side (ADR 0035 decision 2; ticket M4-009). The model's inputs are the
/// product's shared inputs (ADR 0021), so they are bound back to each side's parameters by the rule
/// <c>ProductEncoder.Pair</c> uses. A C# parameter of a type the M3-032 generators build becomes its wire value: a
/// <c>bool</c>, an integer, a <c>char</c> or an enum from its bitvector; a <c>string</c> from its sort element, <c>null</c>
/// when the model's <c>null.&lt;Sort&gt;</c> map says so and otherwise <c>"s&lt;id&gt;"</c>, so that equal elements are
/// equal strings; a <c>float</c>, <c>double</c> or <c>decimal</c> as the number <c>id</c>; any other reference type only
/// as <c>null</c>. The receiver is <c>new T()</c>. Anything else, and every synthesised input other than <c>this</c> and
/// <c>null.*</c> (a heap map, a cast or type-test map, <c>typeof</c>, <c>new</c>), makes the case not constructible.
/// </summary>
internal static class ReplayArguments
{
    private const string NullPrefix = "null.";

    /// <summary>One side's case, or null with the reason it cannot be built.</summary>
    internal sealed record SideCase(ExecutionInput? Input, string Reason);

    /// <summary>
    /// Each side's parameter values by name, from <paramref name="inputs"/>: the old side's parameters in order, each also
    /// bound to its new counterpart (by position for a C# parameter, by name for a synthesised one) when the types are equal,
    /// then the new side's unpaired parameters. Null when the model is not over these two parameter lists.
    /// </summary>
    public static (Dictionary<string, IrValue> Old, Dictionary<string, IrValue> New)? Bind(IrProcedure old, IrProcedure @new, IrInputs inputs)
    {
        IrParameter[] positional = [.. @new.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name))];
        Dictionary<string, IrParameter> synthesised = @new.Parameters
            .Where(static p => IrParameterNames.IsSynthesised(p.Var.Name))
            .ToDictionary(static p => p.Var.Name, StringComparer.Ordinal);
        Dictionary<string, IrValue> oldValues = new(StringComparer.Ordinal);
        Dictionary<string, IrValue> newValues = new(StringComparer.Ordinal);
        Queue<IrValue> values = new(inputs.Arguments);
        int position = 0;
        foreach (IrParameter parameter in old.Parameters)
        {
            IrParameter? counterpart = IrParameterNames.IsSynthesised(parameter.Var.Name)
                ? synthesised.GetValueOrDefault(parameter.Var.Name)
                : positional.ElementAtOrDefault(position++);
            if (!Take(values, parameter, oldValues))
            {
                return null;
            }

            if (counterpart is not null && counterpart.Var.Type == parameter.Var.Type)
            {
                newValues[counterpart.Var.Name] = oldValues[parameter.Var.Name];
            }
        }

        foreach (IrParameter parameter in @new.Parameters.Where(p => !newValues.ContainsKey(p.Var.Name)))
        {
            if (!Take(values, parameter, newValues))
            {
                return null;
            }
        }

        return values.Count == 0 ? (oldValues, newValues) : null;
    }

    /// <summary>
    /// The case that calls <paramref name="method"/>, lowered as <paramref name="body"/>, with <paramref name="values"/>;
    /// <paramref name="nullness"/> holds every <c>null.*</c> map of either side.
    /// </summary>
    public static SideCase Case(IMethodSymbol method, IrProcedure body, Dictionary<string, IrValue> values, IReadOnlyDictionary<string, IrValue> nullness)
    {
        if (Obstacle(method, body, values, nullness) is { } obstacle)
        {
            return new SideCase(Input: null, obstacle);
        }

        IrParameter[] parameters = [.. body.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name))];
        List<string> arguments = [];
        foreach ((IParameterSymbol parameter, IrParameter lowered) in method.Parameters.Zip(parameters))
        {
            if (Wire(parameter.Type, values[lowered.Var.Name], nullness) is not { } argument)
            {
                return new SideCase(Input: null, $"no {parameter.Type.ToDisplayString()} argument can be built for {parameter.Name} from the model");
            }

            arguments.Add(argument);
        }

        return new SideCase(new ExecutionInput(arguments), string.Empty);
    }

    /// <summary>Every <c>null.*</c> map in <paramref name="sides"/>, by name.</summary>
    public static Dictionary<string, IrValue> Nullness(params IEnumerable<Dictionary<string, IrValue>> sides)
    {
        Dictionary<string, IrValue> nullness = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IrValue> value in sides.SelectMany(static s => s).Where(static v => v.Key.StartsWith(NullPrefix, StringComparison.Ordinal)))
        {
            nullness.TryAdd(value.Key, value.Value);
        }

        return nullness;
    }

    private static bool Take(Queue<IrValue> values, IrParameter parameter, Dictionary<string, IrValue> bound)
    {
        if (!values.TryDequeue(out IrValue? value) || value.Type != parameter.Var.Type)
        {
            return false;
        }

        bound[parameter.Var.Name] = value;
        return true;
    }

    /// <summary>Why generated source cannot call <paramref name="method"/> with the model's values, whatever its arguments.</summary>
    private static string? Obstacle(IMethodSymbol method, IrProcedure body, Dictionary<string, IrValue> values, IReadOnlyDictionary<string, IrValue> nullness)
    {
        string[] constrained =
        [
            .. body.Parameters.Select(static p => p.Var.Name)
                .Where(static n => IrParameterNames.IsSynthesised(n) && !string.Equals(n, IrParameterNames.Receiver, StringComparison.Ordinal) && !n.StartsWith(NullPrefix, StringComparison.Ordinal)),
        ];
        return method switch
        {
            _ when !IsPublic(method) => "not public",
            { MethodKind: not (MethodKind.Ordinary or MethodKind.PropertyGet) } => "not a method or a property getter",
            _ when method.IsGenericMethod || method.ContainingType.IsGenericType => "generic",
            _ when method.Parameters.FirstOrDefault(static p => p.RefKind != RefKind.None) is { } byRef => $"{byRef.Name} is passed by reference",
            _ when constrained.Length > 0 => $"the model constrains {string.Join(", ", constrained)}",
            { IsStatic: false } => ReceiverObstacle(method.ContainingType, values, nullness),
            _ => null,
        };
    }

    private static string? ReceiverObstacle(INamedTypeSymbol type, Dictionary<string, IrValue> values, IReadOnlyDictionary<string, IrValue> nullness)
    {
        bool constructible = !type.IsAbstract && (type.IsValueType || type.InstanceConstructors.Any(static c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public));
        if (!constructible)
        {
            return $"{type.ToDisplayString()} has no public parameterless constructor";
        }

        return values.GetValueOrDefault(IrParameterNames.Receiver) is IrSortValue receiver && IsNull(receiver, nullness) ? "the model's receiver is null" : null;
    }

    private static bool IsPublic(ISymbol symbol) =>
        symbol.DeclaredAccessibility == Accessibility.Public && (symbol.ContainingType is null || IsPublic(symbol.ContainingType));

    /// <summary><paramref name="value"/> as a wire argument of <paramref name="type"/>, or null when none can be built from it.</summary>
    private static string? Wire(ITypeSymbol type, IrValue value, IReadOnlyDictionary<string, IrValue> nullness)
    {
        ExecutionTypeKind kind = DriverFactory.Classify(type);
        return value switch
        {
            IrBoolValue b when kind == ExecutionTypeKind.Boolean => b.Value ? "true" : "false",
            IrBitVecValue v when kind is >= ExecutionTypeKind.Character and <= ExecutionTypeKind.Unsigned64 => Integer(type, v),
            IrBitVecValue v when kind == ExecutionTypeKind.Enum => Integer(((INamedTypeSymbol)type).EnumUnderlyingType!, v),
            IrSortValue s => Element(kind, s, nullness),
            _ => null,
        };
    }

    /// <summary>A sort element as a number, a string or <c>null</c>.</summary>
    private static string? Element(ExecutionTypeKind kind, IrSortValue value, IReadOnlyDictionary<string, IrValue> nullness) => kind switch
    {
        ExecutionTypeKind.Binary32 => Bits(BitConverter.SingleToUInt32Bits(value.Id), "X8"),
        ExecutionTypeKind.Binary64 => Bits(BitConverter.DoubleToUInt64Bits(value.Id), "X16"),
        ExecutionTypeKind.DecimalNumber => string.Create(CultureInfo.InvariantCulture, $"[{value.Id},0,0,0]"),
        ExecutionTypeKind.Text => IsNull(value, nullness) ? "null" : string.Create(CultureInfo.InvariantCulture, $"\"s{value.Id}\""),
        ExecutionTypeKind.NullOnly when IsNull(value, nullness) => "null",
        _ => null,
    };

    /// <summary>A bitvector as <paramref name="type"/>'s integer: two's complement for a signed type, the magnitude otherwise.</summary>
    private static string Integer(ITypeSymbol type, IrBitVecValue value) =>
        type.SpecialType is SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64
            ? value.TwosComplement.ToString(CultureInfo.InvariantCulture)
            : value.Bits.ToString(CultureInfo.InvariantCulture);

    private static string Bits(ulong bits, string format) => $"\"0x{bits.ToString(format, CultureInfo.InvariantCulture)}\"";

    /// <summary>Whether the model's <c>null.&lt;Sort&gt;</c> map holds <paramref name="value"/>; with no map, it is not null.</summary>
    private static bool IsNull(IrSortValue value, IReadOnlyDictionary<string, IrValue> nullness) =>
        nullness.GetValueOrDefault(NullPrefix + value.Sort) is IrMapValue map && map.Read(value) is IrBoolValue { Value: true };
}
