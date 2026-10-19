using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Turns a Divergent's model into one driver case per side (ADR 0035 decision 2; ticket M4-009). The model's inputs are the
/// product's shared inputs (ADR 0021), so they are bound back to each side's parameters by the rule
/// <c>ProductEncoder.Pair</c> uses. A C# parameter of a type the M3-032 generators build becomes its wire value: a
/// <c>bool</c>, an integer, a <c>char</c> or an enum from its bitvector; a <c>string</c> from its sort element, <c>null</c>
/// when the model's <c>null.&lt;Sort&gt;</c> map says so and otherwise <c>"s&lt;id&gt;"</c>, so that equal elements are
/// equal strings; a <c>float</c>, <c>double</c> or <c>decimal</c> as the number <c>id</c>. A receiver, an object, an
/// array, a struct, a <c>CancellationToken</c> and a delegate are built from the model's heap by
/// <see cref="ModelObjects"/> (ADR 0062 decisions 2 and 3; ticket P2-245), from both sides' synthesised inputs, so that
/// the two sides get one state; the receiver is <c>new T()</c> when the model leaves it free on both sides and <c>T</c>
/// has a public parameterless constructor. A constructor has no receiver, and its parameters are bound as a method's
/// (ticket P2-242). A member the friend assembly cannot name is reached as ADR 0062 decision 1 says
/// (<see cref="DriverSource.Access"/>).
/// </summary>
internal static class ReplayArguments
{
    private const string NullPrefix = "null.";

    /// <summary>
    /// One side's case, or null with the reason it cannot be built. <see cref="Input"/> is the case for the side's own
    /// receiver choice (<see cref="Mode"/>); a pair whose other side needs its receiver from the wire takes
    /// <see cref="For"/> <see cref="DriverSource.Receiver.Wire"/> instead, so both sides build their receivers alike.
    /// </summary>
    internal sealed record SideCase(ExecutionInput? Input, string Reason)
    {
        /// <summary>The types and makers the case's wire names.</summary>
        public DriverHeap Heap { get; init; } = new();

        /// <summary>The receiver built from the wire, or null for a member with none.</summary>
        public ModelObjects.Built? Receiver { get; init; }

        /// <summary>The constructor that builds the receiver when <c>new T()</c> is its state too; null when it is not.</summary>
        public string? NewReceiver { get; init; }

        /// <summary>Each parameter's name and built argument, in order.</summary>
        public IReadOnlyList<(string Name, ModelObjects.Built Built)> Arguments { get; init; } = [];

        /// <summary>The side's own choice: <c>new T()</c> when it can be, else the receiver from the wire.</summary>
        public DriverSource.Receiver Mode => Receiver is not null && NewReceiver is null ? DriverSource.Receiver.Wire : DriverSource.Receiver.New;

        /// <summary>The case's wire arguments when its receiver is built as <paramref name="mode"/> says: the receiver first from the wire.</summary>
        public ExecutionInput For(DriverSource.Receiver mode) =>
            new([.. (mode == DriverSource.Receiver.Wire && Receiver is { } receiver ? [receiver.Wire] : Array.Empty<string>()), .. Arguments.Select(static a => a.Built.Wire)]);

        /// <summary>How each input was built (ADR 0062 decision 5), the receiver first, when it is built as <paramref name="mode"/> says.</summary>
        public IReadOnlyList<ReplayInput> Provenance(DriverSource.Receiver mode)
        {
            List<ReplayInput> inputs = [];
            if (Receiver is { } receiver)
            {
                inputs.Add(new ReplayInput(IrParameterNames.Receiver, mode == DriverSource.Receiver.New ? $"constructor {NewReceiver}" : receiver.How));
            }

            inputs.AddRange(Arguments.Select(static a => new ReplayInput(a.Name, a.Built.How)));
            return inputs;
        }

        /// <summary>The case built from the parts a builder made, its <see cref="Input"/> for its own receiver choice.</summary>
        public static SideCase Of(DriverHeap heap, ModelObjects.Built? receiver, string? newReceiver, IReadOnlyList<(string, ModelObjects.Built)> arguments)
        {
            SideCase parts = new(Input: null, string.Empty) { Heap = heap, Receiver = receiver, NewReceiver = newReceiver, Arguments = arguments };
            return parts with { Input = parts.For(parts.Mode) };
        }
    }

    /// <summary>How both sides build their receivers: <c>new T()</c> only when both can, so the two have one state.</summary>
    public static DriverSource.Receiver Mode(SideCase old, SideCase @new) =>
        old.Mode == DriverSource.Receiver.Wire || @new.Mode == DriverSource.Receiver.Wire ? DriverSource.Receiver.Wire : DriverSource.Receiver.New;

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

        return !@new.Parameters.Any(parameter => !newValues.ContainsKey(parameter.Var.Name) && !Take(values, parameter, newValues)) && values.Count == 0
            ? (oldValues, newValues)
            : null;
    }

    /// <summary>
    /// The case that calls <paramref name="method"/>, lowered as <paramref name="body"/>, of a project compiled as
    /// <paramref name="compilation"/>, with <paramref name="values"/>; <paramref name="heap"/> holds every synthesised input
    /// of either side but the receiver, and <paramref name="model"/> the model's runs, which say whether a delegate is
    /// called and a token cancelled.
    /// </summary>
    public static SideCase Case(IMethodSymbol method, Compilation compilation, IrProcedure body, Dictionary<string, IrValue> values, IReadOnlyDictionary<string, IrValue> heap, Counterexample? model = null)
    {
        if (CallObstacle(method, reach: true) is { } obstacle)
        {
            return new SideCase(Input: null, obstacle);
        }

        DriverHeap driver = new();
        (ModelObjects? objects, string constrained) = ModelObjects.Over(compilation, heap, model, driver);
        IrValue self = values.GetValueOrDefault(IrParameterNames.Receiver) ?? Unconstrained(method.ContainingType);
        if (objects is null || (self is IrSortValue receiver && IsNull(receiver, heap)))
        {
            return new SideCase(Input: null, objects is null ? constrained : "the model's receiver is null");
        }

        List<(string Name, ITypeSymbol Type, IrValue? Value)> roots = [.. method.Parameters.Zip(body.Parameters.Where(static p => !IrParameterNames.IsSynthesised(p.Var.Name)), (p, l) => (p.Name, p.Type, values.GetValueOrDefault(l.Var.Name)))];
        if (DriverFactory.HasReceiver(method))
        {
            roots.Insert(0, (IrParameterNames.Receiver, method.ContainingType, self));
        }

        roots.ForEach(r => objects.Discover(r.Type, r.Value));
        List<(string, ModelObjects.Built)> built = [];
        foreach ((string name, ITypeSymbol type, IrValue? value) in roots)
        {
            if (objects.Input(type, value, name) is not { } input)
            {
                return new SideCase(Input: null, objects.Reason);
            }

            built.Add((name, input));
        }

        objects.CheckFresh();
        if (objects.Reason.Length > 0)
        {
            return new SideCase(Input: null, objects.Reason);
        }

        bool hasReceiver = DriverFactory.HasReceiver(method);
        string? newReceiver = hasReceiver && objects.Free(method.ContainingType, self) ? PublicParameterless(method.ContainingType)?.ToDisplayString() : null;
        return SideCase.Of(driver, hasReceiver ? built[0].Item2 : null, newReceiver, hasReceiver ? built[1..] : built);
    }

    /// <summary>A receiver the model does not name: an element no map of the model holds, so every map gives it its default.</summary>
    private static IrSortValue Unconstrained(INamedTypeSymbol type) => new(Lowering.TypeMapper.MetadataName(type), -1);

    /// <summary>Every synthesised input of <paramref name="sides"/> but the receiver, by name: the model's heap both sides are built from.</summary>
    public static Dictionary<string, IrValue> Heap(params IEnumerable<Dictionary<string, IrValue>> sides)
    {
        Dictionary<string, IrValue> heap = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IrValue> value in sides.SelectMany(static s => s).Where(static v => IrParameterNames.IsSynthesised(v.Key) && !string.Equals(v.Key, IrParameterNames.Receiver, StringComparison.Ordinal)))
        {
            heap.TryAdd(value.Key, value.Value);
        }

        return heap;
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

    /// <summary>
    /// Why generated source cannot call <paramref name="method"/> at all, whatever its arguments, or null (tickets M4-009,
    /// P1-008). A constructor is called like a static method that returns the new object (ticket P2-242), unless its type is
    /// abstract. The driver is a friend of the emitted project (<see cref="ProjectEmitter"/>; ticket P2-052), so an
    /// <c>internal</c> or <c>protected internal</c> member is called like a public one. A <c>private</c>, <c>protected</c>
    /// or <c>private protected</c> member, or a member of such a type, is <c>not public</c>, with the accessibility that hides
    /// it in brackets, unless <paramref name="reach"/> is set: a replay or <c>probe</c> driver then reaches a method or
    /// getter by its exact signature (ADR 0062 decision 1), builds a receiver with no public parameterless constructor
    /// (decision 2), and refuses a window, as a runtime-diff driver does (ADR 0035). An <c>extern</c> method is never called
    /// (ADR 0054 decision 6; ticket P2-145): a driver would hand made-up handles, pointers and lengths straight to native code.
    /// </summary>
    public static string? CallObstacle(IMethodSymbol method, bool reach = false)
    {
        if (Hidden(method) is { } accessibility && (!reach || method.MethodKind == MethodKind.Constructor))
        {
            return $"not public ({accessibility})";
        }

        if (method.IsExtern)
        {
            return "extern";
        }

        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.Constructor))
        {
            return "not a method, a property getter or a constructor";
        }

        if (method.MethodKind == MethodKind.Constructor && method.ContainingType.IsAbstract)
        {
            return "constructor of an abstract type";
        }

        if (method.IsGenericMethod || method.ContainingType.IsGenericType)
        {
            return "generic";
        }

        IParameterSymbol? byRef = method.Parameters.FirstOrDefault(static p => p.RefKind != RefKind.None);
        return (byRef, reach, !DriverFactory.HasReceiver(method) || Constructible(method.ContainingType)) switch
        {
            ({ } parameter, _, _) => $"{parameter.Name} is passed by reference",
            (null, true, _) => Reached(method),
            (null, false, false) => $"{method.ContainingType.ToDisplayString()} has no public parameterless constructor",
            _ => null,
        };
    }

    /// <summary>What still keeps a replay or <c>probe</c> driver from <paramref name="method"/>: a window, or a signature no driver can bind.</summary>
    private static string? Reached(IMethodSymbol method) =>
        DriverFactory.IsDesktopSession(method.ContainingType) || (DriverFactory.HasReceiver(method) && DriverFactory.IsWindow(method.ContainingType)) || method.Parameters.Any(static p => DriverFactory.IsWindow(p.Type))
            ? DriverFactory.NeedsWindow
            : DriverSource.Unbound(method);

    private static bool Constructible(INamedTypeSymbol type) => !type.IsAbstract && (type.IsValueType || PublicParameterless(type) is not null);

    /// <summary>The public parameterless constructor of <paramref name="type"/>, a struct's implicit one included; null when it has none.</summary>
    internal static IMethodSymbol? PublicParameterless(INamedTypeSymbol type) =>
        type.IsAbstract ? null : type.InstanceConstructors.FirstOrDefault(static c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);

    /// <summary>The accessibility that hides <paramref name="symbol"/>, or the outermost type that hides it, from a friend assembly; null when it is visible.</summary>
    internal static string? Hidden(ISymbol symbol)
    {
        string? outer = symbol.ContainingType is null ? null : Hidden(symbol.ContainingType);
        return outer ?? symbol.DeclaredAccessibility switch
        {
            Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal => null,
            var accessibility => SyntaxFacts.GetText(accessibility),
        };
    }

    /// <summary><paramref name="value"/> as a wire argument of the scalar <paramref name="type"/>, or null when none can be built from it.</summary>
    internal static string? Wire(ITypeSymbol type, IrValue value, IReadOnlyDictionary<string, IrValue> nullness)
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
        ExecutionTypeKind.Binary32 => Bits((uint)value.Id, "X8"),
        ExecutionTypeKind.Binary64 => Bits((ulong)value.Id, "X16"),
        ExecutionTypeKind.DecimalNumber => string.Create(CultureInfo.InvariantCulture, $"[{value.Id},0,0,0]"),
        ExecutionTypeKind.Text => IsNull(value, nullness) ? "null" : string.Create(CultureInfo.InvariantCulture, $"\"s{value.Id}\""),
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
