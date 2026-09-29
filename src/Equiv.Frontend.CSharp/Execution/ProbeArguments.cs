using System.Globalization;
using System.Text;
using System.Text.Json;

using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Turns an agent's own JSON arguments into one driver case for <paramref name="method"/> (ADR 0035, ADR 0036; ticket
/// M5-002's <c>probe</c>). Unlike <see cref="ReplayArguments"/>, there is no model to bind: <paramref name="arguments"/>
/// are read positionally against <paramref name="method"/>'s own parameters, its receiver excluded (the receiver is
/// <c>new T()</c>, as replay's is). A JSON value that does not fit its parameter's kind, or an argument count that does not
/// match, makes the case not constructible, naming the parameter.
/// </summary>
internal static class ProbeArguments
{
    /// <summary>One side's case, or null with the reason it cannot be built.</summary>
    internal sealed record SideCase(ExecutionInput? Input, string Reason);

    public static SideCase Case(IMethodSymbol method, IReadOnlyList<JsonElement> arguments)
    {
        if (ReplayArguments.CallObstacle(method) is { } obstacle)
        {
            return new SideCase(Input: null, obstacle);
        }

        if (arguments.Count != method.Parameters.Length)
        {
            return new SideCase(Input: null, $"expected {method.Parameters.Length} argument(s), got {arguments.Count}");
        }

        List<string> wire = [];
        foreach ((IParameterSymbol parameter, JsonElement value) in method.Parameters.Zip(arguments))
        {
            if (Wire(parameter.Type, value) is not { } argument)
            {
                return new SideCase(Input: null, $"no {parameter.Type.ToDisplayString()} argument can be built for {parameter.Name} from {value.GetRawText()}");
            }

            wire.Add(argument);
        }

        return new SideCase(new ExecutionInput(wire), string.Empty);
    }

    /// <summary><paramref name="value"/> as a wire argument of <paramref name="type"/>, or null when none can be built from it.</summary>
    private static string? Wire(ITypeSymbol type, JsonElement value) => DriverFactory.Classify(type) switch
    {
        ExecutionTypeKind.Boolean => value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null },
        ExecutionTypeKind.Character or ExecutionTypeKind.UnsignedByte or ExecutionTypeKind.Unsigned16 or ExecutionTypeKind.Unsigned32 or ExecutionTypeKind.Unsigned64
            => value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong u) ? u.ToString(CultureInfo.InvariantCulture) : null,
        ExecutionTypeKind.SignedByte or ExecutionTypeKind.Signed16 or ExecutionTypeKind.Signed32 or ExecutionTypeKind.Signed64
            => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long i) ? i.ToString(CultureInfo.InvariantCulture) : null,
        ExecutionTypeKind.Binary32 => value.ValueKind == JsonValueKind.Number ? Bits(BitConverter.SingleToUInt32Bits(value.GetSingle()), "X8") : null,
        ExecutionTypeKind.Binary64 => value.ValueKind == JsonValueKind.Number ? Bits(BitConverter.DoubleToUInt64Bits(value.GetDouble()), "X16") : null,
        ExecutionTypeKind.DecimalNumber => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal m) ? Decimal(m) : null,
        ExecutionTypeKind.Text => Text(value),
        ExecutionTypeKind.Enum => Enum(type, value),
        ExecutionTypeKind.NullOnly => value.ValueKind == JsonValueKind.Null ? "null" : null,
        _ => null,
    };

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Escape(value.GetString()!),
        JsonValueKind.Null => "null",
        _ => null,
    };

    /// <summary>An enum from its member name or its underlying integer, whichever the JSON value gives.</summary>
    private static string? Enum(ITypeSymbol type, JsonElement value)
    {
        INamedTypeSymbol enumType = (INamedTypeSymbol)type;
        bool unsigned = IsUnsigned(enumType.EnumUnderlyingType!);
        if (value.ValueKind == JsonValueKind.String)
        {
            IFieldSymbol? member = enumType.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && string.Equals(f.Name, value.GetString(), StringComparison.Ordinal));
            if (member is null)
            {
                return null;
            }

            return unsigned
                ? Convert.ToUInt64(member.ConstantValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                : Convert.ToInt64(member.ConstantValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (unsigned)
        {
            return value.TryGetUInt64(out ulong u) ? u.ToString(CultureInfo.InvariantCulture) : null;
        }

        return value.TryGetInt64(out long i) ? i.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static bool IsUnsigned(ITypeSymbol underlying) =>
        underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    /// <summary>The driver's decimal wire form: <c>decimal.GetBits</c>' four integers, each a signed decimal string.</summary>
    private static string Decimal(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        return string.Create(CultureInfo.InvariantCulture, $"[{bits[0]},{bits[1]},{bits[2]},{bits[3]}]");
    }

    private static string Bits(ulong bits, string format) => $"\"0x{bits.ToString(format, CultureInfo.InvariantCulture)}\"";

    /// <summary>
    /// A JSON string as the driver's own minimal JSON text: only <c>\"</c>, <c>\\</c> and <c>\uXXXX</c> are escapes, so the
    /// driver's hand-written parser (which does not decode <c>\n</c>-style escapes) reads it back unchanged.
    /// </summary>
    private static string Escape(string value)
    {
        StringBuilder text = new("\"");
        foreach (char c in value)
        {
            if (c is '"' or '\\')
            {
                text.Append('\\').Append(c);
            }
            else if (c is < ' ' or > '~')
            {
                text.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.Append('"').ToString();
    }
}
