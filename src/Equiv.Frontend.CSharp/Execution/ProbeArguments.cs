using System.Globalization;
using System.Text.Json;

using Equiv.Core.Execution;
using Equiv.Core.Ir;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Turns an agent's own JSON arguments into one driver case for a method (ADR 0035, ADR 0036; ticket M5-002's
/// <c>probe</c>). Unlike <see cref="ReplayArguments"/>, there is no model to bind: the arguments are read positionally
/// against the method's own parameters. A flat struct (<see cref="DriverStruct"/>) is a JSON object by field or
/// auto-property name (tickets P2-242 and P2-246), a nested struct a nested object. ADR 0062 decision 4 (ticket P2-245)
/// adds the inputs a replay builds from a model, by name: an array is a JSON array; a <c>CancellationToken</c> is
/// <c>"cancelled"</c> or <c>"live"</c>; a delegate is <c>{"returns": value}</c>, answering every call with that value
/// (<c>null</c> for one returning nothing); an object is <c>null</c>, <c>{"constructor": "int, string", "arguments":
/// [...]}</c> naming a visible constructor by its parameter types, or <c>{"fields": {...}}</c>, a solution type created
/// without a constructor with those fields, by field or auto-property name, of <c>"type"</c> when the object names one;
/// a solution struct that is not flat is a JSON object of its fields, built the same way. The receiver is
/// <c>new T()</c> when none is given and <c>T</c> has a public parameterless constructor, a solution type created without
/// one otherwise, or the given object. A JSON value that does not fit its parameter's kind, or an argument count that does
/// not match, makes the case not constructible, naming the parameter.
/// </summary>
internal static class ProbeArguments
{
    /// <summary>The case for <paramref name="method"/> with <paramref name="arguments"/> and, for an instance member, <paramref name="receiver"/>.</summary>
    public static ReplayArguments.SideCase Case(IMethodSymbol method, IReadOnlyList<JsonElement> arguments, JsonElement? receiver = null)
    {
        if (ReplayArguments.CallObstacle(method, reach: true) is { } obstacle)
        {
            return new ReplayArguments.SideCase(Input: null, obstacle);
        }

        if (arguments.Count != method.Parameters.Length)
        {
            return new ReplayArguments.SideCase(Input: null, $"expected {method.Parameters.Length} argument(s), got {arguments.Count}");
        }

        Builder builder = new(method.ContainingAssembly, new DriverHeap());
        (ModelObjects.Built? self, string? newReceiver) = (null, null);
        if (DriverFactory.HasReceiver(method))
        {
            (self, newReceiver) = builder.Receiver(method.ContainingType, receiver);
            if (self is null)
            {
                return new ReplayArguments.SideCase(Input: null, builder.Reason);
            }
        }

        List<(string, ModelObjects.Built)> built = [];
        foreach ((IParameterSymbol parameter, JsonElement value) in method.Parameters.Zip(arguments))
        {
            if (builder.Input(parameter.Type, value, parameter.Name) is not { } input)
            {
                return new ReplayArguments.SideCase(Input: null, builder.Reason);
            }

            built.Add((parameter.Name, input));
        }

        return ReplayArguments.SideCase.Of(builder.Heap, self, newReceiver, built);
    }

    /// <summary>One side's inputs from JSON, into one driver's heap, with how each was built.</summary>
    private sealed class Builder(IAssemblySymbol assembly, DriverHeap heap)
    {
        private readonly List<string> written = [];

        private readonly List<string> constructors = [];

        private readonly List<string> failures = [];

        private bool uninitialized;

        private bool isDelegate;

        public DriverHeap Heap { get; } = heap;

        /// <summary>Why the input cannot be built, the first thing that failed; empty while it can.</summary>
        public string Reason => string.Concat(failures.Take(1));

        /// <summary>
        /// The receiver, of a solution type: the given object, or with none an instance created without a constructor,
        /// beside the <c>new T()</c> its public parameterless constructor builds when it has one.
        /// </summary>
        public (ModelObjects.Built? Self, string? New) Receiver(INamedTypeSymbol type, JsonElement? given)
        {
            if (given is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } value)
            {
                return (Input(type, value, IrParameterNames.Receiver), null);
            }

            ModelObjects.Built? self = type switch
            {
                { IsValueType: true } => new ModelObjects.Built(Heap.Struct(type, []), "uninitialized"),
                { IsAbstract: false } => new ModelObjects.Built(Heap.Uninitialized(Heap.Id(), type, []), "uninitialized"),
                _ => Fail<ModelObjects.Built>($"{type.ToDisplayString()} is abstract: give the receiver by a constructor or by its fields, of a type that derives from it"),
            };
            return (self, ReplayArguments.PublicParameterless(type)?.ToDisplayString());
        }

        /// <summary><paramref name="value"/> as an input of <paramref name="type"/> named <paramref name="name"/>, or null (<see cref="Reason"/> says why).</summary>
        public ModelObjects.Built? Input(ITypeSymbol type, JsonElement value, string name)
        {
            (uninitialized, isDelegate) = (false, false);
            written.Clear();
            constructors.Clear();
            return Value(type, value, name) is { } wire ? new ModelObjects.Built(wire, How) : null;
        }

        private string How => (uninitialized, constructors.Count, isDelegate) switch
        {
            (true, _, _) when written.Count == 0 => "uninitialized",
            (true, _, _) => $"uninitialized {string.Join(", ", written)}",
            (_, > 0, _) => $"constructor {constructors[0]}",
            (_, _, true) => "delegate",
            _ => "value",
        };

        private string? Value(ITypeSymbol type, JsonElement value, string name) => type switch
        {
            _ when DriverFactory.Classify(type) is >= ExecutionTypeKind.Boolean and <= ExecutionTypeKind.Enum
                => Scalar(type, value) ?? Fail($"no {type.ToDisplayString()} argument can be built for {name} from {value.GetRawText()}"),
            _ when string.Equals(type.ToDisplayString(), "System.Threading.CancellationToken", StringComparison.Ordinal) => Token(value, name),
            _ when DriverFactory.IsWindow(type) => Fail($"{name}: {DriverFactory.NeedsWindow}"),
            _ when value.ValueKind == JsonValueKind.Null && !type.IsValueType => "null",
            IArrayTypeSymbol { IsSZArray: true } array when value.ValueKind == JsonValueKind.Array => Array(array, value, name),
            INamedTypeSymbol { TypeKind: TypeKind.Delegate } @delegate when value.ValueKind == JsonValueKind.Object => Delegate(@delegate, value, name),
            { IsValueType: true } when value.ValueKind == JsonValueKind.Object => Struct(type, value, name),
            INamedTypeSymbol { IsReferenceType: true } named when value.ValueKind == JsonValueKind.Object => Object(named, value, name),
            _ => Fail($"no {type.ToDisplayString()} argument can be built for {name} from {value.GetRawText()}"),
        };

        private string? Token(JsonElement value, string name) => value.ValueKind == JsonValueKind.String ? value.GetString() switch
        {
            "cancelled" => DriverHeap.Token(cancelled: true),
            "live" => DriverHeap.Token(cancelled: false),
            _ => Fail($"{name} is a CancellationToken: \"cancelled\" or \"live\", not {value.GetRawText()}"),
        } : Fail($"{name} is a CancellationToken: \"cancelled\" or \"live\", not {value.GetRawText()}");

        private string? Array(IArrayTypeSymbol array, JsonElement value, string name)
        {
            List<string> items = [];
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (Value(array.ElementType, item, $"an element of {name}") is not { } wire)
                {
                    return null;
                }

                items.Add(wire);
            }

            return Heap.Array(Heap.Id(), array.ElementType, items);
        }

        /// <summary>A delegate that answers every call with <c>returns</c>, read against its return type.</summary>
        private string? Delegate(INamedTypeSymbol type, JsonElement value, string name)
        {
            IMethodSymbol invoke = type.DelegateInvokeMethod!;
            if (!value.TryGetProperty("returns", out JsonElement returns) || value.EnumerateObject().Skip(1).Any())
            {
                return Fail($"{name} is a delegate: {{\"returns\": value}}, not {value.GetRawText()}");
            }

            string? answer = invoke.ReturnsVoid
                ? returns.ValueKind == JsonValueKind.Null ? "null" : Fail($"{name} returns nothing, so its answer is null, not {returns.GetRawText()}")
                : Value(invoke.ReturnType, returns, $"{name}'s answer");
            if (answer is null)
            {
                return null;
            }

            isDelegate = true;
            return Heap.Delegate(type, answers: true) is { } maker
                ? DriverHeap.Made(Heap.Id(), maker, [answer])
                : Fail($"no {type.ToDisplayString()} delegate can be built for {name}");
        }

        /// <summary>A flat struct as P2-242 builds it, from its fields by name; a solution struct that is not flat created without a constructor, given its fields.</summary>
        private string? Struct(ITypeSymbol type, JsonElement value, string name)
        {
            if (DriverStruct.Of(type) is { Buildable: true } flat && DriverHeap.Nameable(type))
            {
                string? built = Flat(flat, value, name);
                if (built is null)
                {
                    // P2-242's message names the struct, whichever of its fields did not fit.
                    failures.Insert(0, $"no {type.ToDisplayString()} argument can be built for {name} from {value.GetRawText()}");
                }

                return built;
            }

            if (!IsSolution(type) || type is INamedTypeSymbol { IsGenericType: true })
            {
                return Fail($"no {type.ToDisplayString()} argument can be built for {name} from {value.GetRawText()}");
            }

            uninitialized = true;
            return Fields((INamedTypeSymbol)type, value, name) is { } fields ? Heap.Struct(type, fields) : null;
        }

        /// <summary>
        /// A flat struct from a JSON object with exactly its fields, by name (tickets P2-242 and P2-246), as the JSON array of
        /// their wire values in declaration order; a nested struct's own object becomes a nested array.
        /// </summary>
        private string? Flat(DriverStruct flat, JsonElement value, string name)
        {
            if (value.EnumerateObject().Skip(flat.Fields.Count).Any())
            {
                return Fail($"no struct argument can be built for {name} from {value.GetRawText()}");
            }

            List<string> fields = [];
            foreach (DriverStruct.Field field in flat.Fields)
            {
                if (!value.TryGetProperty(field.Name, out JsonElement member))
                {
                    return Fail($"no struct argument can be built for {name} from {value.GetRawText()}");
                }

                if (Value(field.Type, member, $"{name}.{field.Name}") is not { } wire)
                {
                    return null;
                }

                fields.Add(wire);
            }

            return $"[{string.Join(',', fields)}]";
        }

        /// <summary>An object by a visible constructor (<c>constructor</c>, <c>arguments</c>) or by its fields (<c>fields</c>), of <c>type</c> when it names one.</summary>
        private string? Object(INamedTypeSymbol type, JsonElement value, string name)
        {
            INamedTypeSymbol? target = value.TryGetProperty("type", out JsonElement named)
                ? named.ValueKind == JsonValueKind.String ? assembly.GetTypeByMetadataName(named.GetString()!) : null
                : type;
            if (target is null || !Is(target, type))
            {
                return Fail($"{name}: {(value.TryGetProperty("type", out JsonElement t) ? t.GetRawText() : "its type")} is no {type.ToDisplayString()} this side declares");
            }

            if (DriverFactory.IsWindow(target))
            {
                return Fail($"{name}: {DriverFactory.NeedsWindow}");
            }

            return value.TryGetProperty("constructor", out JsonElement signature)
                ? Constructed(target, signature, value, name)
                : value.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object
                    ? Uninitialized(target, fields, name)
                    : Fail($"{name} is an object: null, {{\"constructor\": \"<parameter types>\", \"arguments\": [...]}} or {{\"fields\": {{...}}}}, not {value.GetRawText()}");
        }

        private string? Constructed(INamedTypeSymbol type, JsonElement signature, JsonElement value, string name)
        {
            string? wanted = signature.ValueKind == JsonValueKind.String ? signature.GetString() : null;
            IMethodSymbol? chosen = type.IsAbstract ? null : type.InstanceConstructors.FirstOrDefault(c => ReplayArguments.Hidden(c) is null
                && string.Equals(string.Join(", ", c.Parameters.Select(static p => p.Type.ToDisplayString())), wanted, StringComparison.Ordinal));
            JsonElement[] arguments = value.TryGetProperty("arguments", out JsonElement given) && given.ValueKind == JsonValueKind.Array ? [.. given.EnumerateArray()] : [];
            if (chosen is null || arguments.Length != chosen.Parameters.Length || Heap.Constructor(chosen) is not { } maker)
            {
                return Fail($"{name}: {type.ToDisplayString()} has no visible constructor ({wanted}) taking {arguments.Length} argument(s)");
            }

            constructors.Add(chosen.ToDisplayString());
            List<string> wires = [];
            foreach ((IParameterSymbol parameter, JsonElement argument) in chosen.Parameters.Zip(arguments))
            {
                if (Value(parameter.Type, argument, $"{name}'s {parameter.Name}") is not { } wire)
                {
                    return null;
                }

                wires.Add(wire);
            }

            return DriverHeap.Made(Heap.Id(), maker, wires);
        }

        private string? Uninitialized(INamedTypeSymbol type, JsonElement fields, string name)
        {
            if (!IsSolution(type) || type.IsAbstract || type.TypeKind != TypeKind.Class || type.IsGenericType)
            {
                return Fail($"{name}: {type.ToDisplayString()} is not a solution class an instance is created of without a constructor");
            }

            uninitialized = true;
            string id = Heap.Id();
            return Fields(type, fields, name) is { } written ? Heap.Uninitialized(id, type, written) : null;
        }

        /// <summary>Each JSON property as the instance field it names on <paramref name="type"/> or a base, written.</summary>
        private List<string>? Fields(INamedTypeSymbol type, JsonElement fields, string name)
        {
            List<string> wires = [];
            foreach (JsonProperty property in fields.EnumerateObject())
            {
                if (Field(type, property.Name) is not { } field)
                {
                    Fail($"{name}: {type.ToDisplayString()} has no field {property.Name}");
                    return null;
                }

                if (Value(field.Type, property.Value, $"{name}.{property.Name}") is not { } wire)
                {
                    return null;
                }

                written.Add($"{field.ContainingType.Name}.{property.Name}");
                wires.Add(Heap.Field(field, wire));
            }

            return wires;
        }

        /// <summary>The instance field <paramref name="name"/> names, by its own name or its auto-property's, on <paramref name="type"/> or the nearest base.</summary>
        private static IFieldSymbol? Field(INamedTypeSymbol type, string name)
        {
            for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                if (t.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic && !f.IsConst
                    && (string.Equals(f.Name, name, StringComparison.Ordinal) || string.Equals((f.AssociatedSymbol as IPropertySymbol)?.Name, name, StringComparison.Ordinal))) is { } field)
                {
                    return field;
                }
            }

            return null;
        }

        private static bool Is(INamedTypeSymbol type, ITypeSymbol target)
        {
            for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t, target))
                {
                    return true;
                }
            }

            return type.AllInterfaces.Contains(target, SymbolEqualityComparer.Default);
        }

        private static bool IsSolution(ITypeSymbol type) => type.Locations.Any(static l => l.IsInSource);

        private string? Fail(string reason) => Fail<string>(reason);

        private T? Fail<T>(string reason)
            where T : class
        {
            failures.Add(reason);
            return null;
        }
    }

    /// <summary><paramref name="value"/> as a wire argument of the scalar <paramref name="type"/>, or null when none can be built from it.</summary>
    private static string? Scalar(ITypeSymbol type, JsonElement value) => DriverFactory.Classify(type) switch
    {
        ExecutionTypeKind.Boolean => Boolean(value),
        ExecutionTypeKind.Character or ExecutionTypeKind.UnsignedByte or ExecutionTypeKind.Unsigned16 or ExecutionTypeKind.Unsigned32 or ExecutionTypeKind.Unsigned64
            => UnsignedNumber(value),
        ExecutionTypeKind.SignedByte or ExecutionTypeKind.Signed16 or ExecutionTypeKind.Signed32 or ExecutionTypeKind.Signed64
            => SignedNumber(value),
        ExecutionTypeKind.Binary32 => Binary32(value),
        ExecutionTypeKind.Binary64 => Binary64(value),
        ExecutionTypeKind.DecimalNumber => DecimalNumber(value),
        ExecutionTypeKind.Text => Text(value),
        _ => Enum(type, value),
    };

    private static string? Boolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };

    private static string? Binary32(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? Bits(BitConverter.SingleToUInt32Bits(value.GetSingle()), "X8") : null;

    private static string? Binary64(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? Bits(BitConverter.DoubleToUInt64Bits(value.GetDouble()), "X16") : null;

    private static string? DecimalNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal m) ? Decimal(m) : null;

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => DriverHeap.Text(value.GetString()!),
        JsonValueKind.Null => "null",
        _ => null,
    };

    /// <summary>An enum from its member name or its underlying integer, whichever the JSON value gives.</summary>
    private static string? Enum(ITypeSymbol type, JsonElement value)
    {
        INamedTypeSymbol enumType = (INamedTypeSymbol)type;
        bool unsigned = IsUnsigned(enumType.EnumUnderlyingType!);
        return value.ValueKind switch
        {
            JsonValueKind.String => EnumByName(enumType, value.GetString(), unsigned),
            JsonValueKind.Number => unsigned ? UnsignedNumber(value) : SignedNumber(value),
            _ => null,
        };
    }

    private static string? EnumByName(INamedTypeSymbol enumType, string? name, bool unsigned)
    {
        IFieldSymbol? member = enumType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && string.Equals(f.Name, name, StringComparison.Ordinal));
        return member is null ? null : EnumConstant(member.ConstantValue, unsigned);
    }

    private static string EnumConstant(object? constant, bool unsigned) => unsigned
        ? Convert.ToUInt64(constant, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
        : Convert.ToInt64(constant, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string? UnsignedNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong u) ? u.ToString(CultureInfo.InvariantCulture) : null;

    private static string? SignedNumber(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long i) ? i.ToString(CultureInfo.InvariantCulture) : null;

    private static bool IsUnsigned(ITypeSymbol underlying) =>
        underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    /// <summary>The driver's decimal wire form: <c>decimal.GetBits</c>' four integers, each a signed decimal string.</summary>
    private static string Decimal(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        return string.Create(CultureInfo.InvariantCulture, $"[{bits[0]},{bits[1]},{bits[2]},{bits[3]}]");
    }

    private static string Bits(ulong bits, string format) => $"\"0x{bits.ToString(format, CultureInfo.InvariantCulture)}\"";
}
