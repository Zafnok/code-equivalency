using System.Globalization;

using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// The C# source of a driver for one member (ADR 0035, ticket M3-032). Its <c>Main</c> reads one JSON line per case on
/// stdin (<c>[culture,arg0,...]</c>), sets the current culture and UI culture, calls the member, and writes one line
/// (<c>["Kind",canonical]</c>) on stdout, which it keeps to itself: <c>Console.Out</c> and <c>Console.Error</c> are
/// pointed at nothing before the first case, so what the code under test prints never reaches the parent (ticket P2-044).
/// The canonical form is written by the driver's own code, never by a runtime's
/// <c>ToString</c>: integers in decimal, <c>float</c> and <c>double</c> as their IEEE bits in hex, <c>decimal</c> as its
/// four <c>GetBits</c> integers, strings as ASCII JSON, <c>char</c> as its code point, an enum as its underlying integer,
/// <c>IntPtr</c> and <c>UIntPtr</c> as their integers, arrays and <c>List&lt;T&gt;</c> of those element-wise, a
/// <c>System.Drawing</c> point, size or rectangle as the array of its components (ticket P2-051), a flat struct
/// (<see cref="DriverStruct"/>) as the array of its fields, a nested struct a nested array (tickets P2-242 and P2-246), and an exception as its type's full name. A
/// constructor's result is the new object. The expression that canonicalises the result is chosen from the member's static
/// return type. An exception that is, or wraps, a Windows Desktop assembly that did not load (<c>L.Desktop</c>) is
/// <c>NotConstructible</c>, never <c>Threw</c>: it says the driver's runtime lacks that assembly, not what the member does
/// (ticket P2-253). A returned <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c> or <c>ValueTask&lt;T&gt;</c> is waited
/// for, and the outcome is the task's: its result's canonical form (<c>null</c> for one with none), the first inner
/// exception's type when it faults, and <c>System.Threading.Tasks.TaskCanceledException</c> when it is cancelled (ticket
/// P2-252). The source is C# 7.3, the newest a .NET Framework side compiles by default, so one template serves every
/// runtime (ticket P2-056).
/// <para>
/// A replay or <c>probe</c> driver (ADR 0062; ticket P2-245) may also reach a member its friend assembly cannot name, bound
/// once by its exact signature through a typed delegate (<see cref="Access.Delegate"/>) or, when the signature names a
/// type the driver cannot, through <c>MethodInfo.Invoke</c> with the inner exception rethrown as thrown
/// (<see cref="Access.Invoke"/>); and it may read an input through <c>V.Value</c>, the value builder of
/// <see cref="DriverHeap"/>'s wire forms, which creates solution instances without a constructor and writes their fields.
/// That generated source, and nothing in <c>src/</c>, uses reflection, and only a driver that needs it carries it.
/// </para>
/// </summary>
internal static class DriverSource
{
    private static readonly SymbolDisplayFormat Qualified = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>Where a driver's receiver comes from.</summary>
    internal enum Receiver
    {
        /// <summary>Argument 0 of the case line, built as any argument is (a runtime-diff member's).</summary>
        Argument,

        /// <summary><c>new T()</c>, by its public parameterless constructor (ticket M4-009's replay).</summary>
        New,

        /// <summary>Argument 0 of the case line, read by the value builder (ADR 0062 decision 2).</summary>
        Wire,
    }

    /// <summary>How a driver reaches its member (ADR 0062 decision 1).</summary>
    internal enum Access
    {
        /// <summary>A call written in source, as a friend sees it.</summary>
        Direct,

        /// <summary>A typed delegate bound to the member by its exact signature: a direct call once bound.</summary>
        Delegate,

        /// <summary><c>MethodInfo.Invoke</c> on the member bound by its exact signature, its inner exception rethrown as thrown.</summary>
        Invoke,
    }

    /// <summary>
    /// What a driver needs besides its member: its receiver, how it reaches the member, the types and makers its wire
    /// names, and whether it creates an instance without a constructor by <c>FormatterServices</c> (.NET Framework, and
    /// .NET Core before 5, whose references have no <c>RuntimeHelpers.GetUninitializedObject</c>).
    /// </summary>
    internal sealed record Shape(Receiver Receiver, Access Access, DriverHeap Heap, bool Formatter)
    {
        /// <summary>A runtime-diff driver: the receiver is argument 0, the member called directly.</summary>
        public static Shape Member => new(Receiver.Argument, Access.Direct, new DriverHeap(), Formatter: false);

        /// <summary>
        /// Whether its inputs are a replay's or a probe's (ADR 0062): a reference-typed argument is then read by the value
        /// builder, which builds an object, an array or a delegate as well as <c>null</c>; otherwise it is only <c>null</c>.
        /// </summary>
        public bool Builds { get; init; }

        /// <summary>
        /// Whether a struct declared in the solution comes as the value builder's instance created without a constructor,
        /// as a replay builds the model's state (decision 2), even where it is flat; a probe sends a flat one as the array of
        /// its fields, built in source as ticket P2-242 builds it.
        /// </summary>
        public bool Uninitializes { get; init; }
    }

    /// <summary>The driver for <paramref name="method"/>, shaped by <paramref name="shape"/> (<see cref="Shape.Member"/> when null).</summary>
    public static string Generate(IMethodSymbol method, Shape? shape = null)
    {
        shape ??= Shape.Member;
        Writer writer = new(shape.Heap, shape.Builds, shape.Uninitializes);
        List<string> arguments = [];
        string? receiver = null;
        int slot = 1;
        if (DriverFactory.HasReceiver(method))
        {
            receiver = shape.Receiver switch
            {
                Receiver.New => writer.Construct(method.ContainingType),
                Receiver.Wire => writer.Read(method.ContainingType, "self", slot++),
                _ => writer.Declare(method.ContainingType, slot++),
            };
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            arguments.Add(writer.Declare(parameter.Type, slot++));
        }

        ITypeSymbol? returned = Result(method);
        bool named = returned is null || DriverHeap.Nameable(returned);
        Awaited? awaited = named ? Awaiting(returned) : null;
        ITypeSymbol? result = awaited is null ? returned : awaited.Result;
        string call = shape.Access == Access.Direct ? Call(method, receiver, arguments) : writer.Bind(method, receiver, arguments, shape.Access);
        string value = (returned, named, shape.Access) switch
        {
            (not null, true, Access.Invoke) => $"({returned.ToDisplayString(Qualified)}){call}",
            _ => call,
        };
        string invoke = (returned, awaited) switch
        {
            (null, _) => $"{call};",
            (_, { Result: null }) => $"A.Wait({awaited.Task(value)});",
            (_, not null) => $"r = A.Get({awaited.Task(value)});",
            _ => $"r = {value};",
        };
        string declareResult = result is null ? string.Empty : $"{(named ? result.ToDisplayString(Qualified) : "object")} r;";
        string answer = (result, named) switch
        {
            (null, _) => "Returned(\"null\")",
            (_, true) => Answer(result),
            _ => $"NotComparable({SymbolDisplay.FormatLiteral(result.ToDisplayString(), quote: true)})",
        };
        return Template
            .Replace("/*DECLARATIONS*/", string.Concat(writer.Declarations), StringComparison.Ordinal)
            .Replace("/*RESULT*/", declareResult, StringComparison.Ordinal)
            .Replace("/*CALL*/", invoke, StringComparison.Ordinal)
            .Replace("/*ANSWER*/", answer, StringComparison.Ordinal)
            .Replace("/*TASKS*/", awaited is null ? string.Empty : TaskTemplate, StringComparison.Ordinal)
            .Replace("/*HELPERS*/", writer.UsesHelpers ? Helpers(shape) : string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// A task a driver waits for (ticket P2-252): the type of its result, null for a <c>Task</c> or <c>ValueTask</c>, and
    /// whether it is a <c>ValueTask</c>, which the driver waits for as its <c>AsTask()</c>.
    /// </summary>
    private sealed record Awaited(ITypeSymbol? Result, bool Value)
    {
        /// <summary>The <c>Task</c> that <paramref name="value"/>, an expression of the awaited type, completes with.</summary>
        public string Task(string value) => Value ? $"({value}).AsTask()" : value;
    }

    /// <summary>
    /// <paramref name="type"/> as a task a driver waits for, or null when it is none: <c>Task</c>, <c>Task&lt;T&gt;</c>,
    /// <c>ValueTask</c> and <c>ValueTask&lt;T&gt;</c> of <c>System.Threading.Tasks</c>.
    /// </summary>
    private static Awaited? Awaiting(ITypeSymbol? type) => type?.OriginalDefinition.ToDisplayString() switch
    {
        "System.Threading.Tasks.Task" => new Awaited(Result: null, Value: false),
        "System.Threading.Tasks.ValueTask" => new Awaited(Result: null, Value: true),
        "System.Threading.Tasks.Task<TResult>" => new Awaited(((INamedTypeSymbol)type).TypeArguments[0], Value: false),
        "System.Threading.Tasks.ValueTask<TResult>" => new Awaited(((INamedTypeSymbol)type).TypeArguments[0], Value: true),
        _ => null,
    };

    /// <summary>What a call yields: a constructor its type, a method its return type, and a <c>void</c> method nothing.</summary>
    private static ITypeSymbol? Result(IMethodSymbol method) => method switch
    {
        { MethodKind: MethodKind.Constructor } => method.ContainingType,
        { ReturnsVoid: true } => null,
        _ => method.ReturnType,
    };

    /// <summary>The driver's answer for a returned <paramref name="result"/>: its canonical form, or NotComparable with its type.</summary>
    private static string Answer(ITypeSymbol result) => Canonical(result, "r", 0) is { } canonical
        ? $"Returned({canonical})"
        : $"NotComparable({SymbolDisplay.FormatLiteral(result.ToDisplayString(), quote: true)})";

    /// <summary>
    /// Why <paramref name="method"/> cannot be bound by its exact signature, naming it, or null when every type in that
    /// signature can be spelled as a <c>System.Type</c> in a driver (ADR 0062 decision 1).
    /// </summary>
    internal static string? Unbound(IMethodSymbol method) =>
        method.Parameters.Select(static p => p.Type).Prepend(method.ContainingType).Append(method.ReturnType).FirstOrDefault(static t => t.SpecialType != SpecialType.System_Void && DriverHeap.TypeOf(t) is null) is { } type
            ? $"{method.ToDisplayString()} is not bound by its exact signature: no driver can name {type.ToDisplayString()}"
            : null;

    /// <summary>
    /// Whether a typed delegate can call <paramref name="method"/>: it is not a constructor, every type in its signature can
    /// be named, its receiver, if any, is a class (an open delegate over a struct takes it by reference), and it has at most
    /// sixteen inputs.
    /// </summary>
    internal static bool Delegable(IMethodSymbol method) =>
        method.MethodKind != MethodKind.Constructor
        && DriverHeap.Nameable(method.ContainingType)
        && method.Parameters.All(static p => DriverHeap.Nameable(p.Type))
        && (method.ReturnsVoid || DriverHeap.Nameable(method.ReturnType))
        && !(DriverFactory.HasReceiver(method) && method.ContainingType.IsValueType)
        && method.Parameters.Length + (DriverFactory.HasReceiver(method) ? 1 : 0) <= 16;

    /// <summary>
    /// How both sides' drivers reach their members (ADR 0062 decision 1): directly when a friend can name both, else both
    /// by one mechanism, a typed delegate when both allow one and <c>Invoke</c> otherwise, so the two calls have one shape.
    /// </summary>
    internal static Access Reach(IMethodSymbol old, IMethodSymbol @new) =>
        (ReplayArguments.Hidden(old) ?? ReplayArguments.Hidden(@new), Delegable(old) && Delegable(@new)) switch
        {
            (null, _) => Access.Direct,
            (_, true) => Access.Delegate,
            _ => Access.Invoke,
        };

    /// <summary>
    /// A C# expression that builds a <paramref name="type"/> from the parsed wire value <paramref name="value"/>, or null when
    /// only the value builder can: a type the driver cannot name, and, when it <paramref name="builds"/>, a reference type.
    /// </summary>
    private static string? Decode(ITypeSymbol type, string value, bool builds)
    {
        string display = type.ToDisplayString(Qualified);
        return DriverFactory.Classify(type) switch
        {
            _ when !DriverHeap.Nameable(type) => null,
            ExecutionTypeKind.NullOnly => builds ? null : $"({display}){value}",
            ExecutionTypeKind.Boolean => $"R.B({value})",
            ExecutionTypeKind.Character or ExecutionTypeKind.UnsignedByte or ExecutionTypeKind.Unsigned16 or ExecutionTypeKind.Unsigned32 or ExecutionTypeKind.Unsigned64
                => $"checked(({display})R.U({value}))",
            ExecutionTypeKind.SignedByte or ExecutionTypeKind.Signed16 or ExecutionTypeKind.Signed32 or ExecutionTypeKind.Signed64
                => $"checked(({display})R.I({value}))",
            ExecutionTypeKind.Binary32 => $"R.F({value})",
            ExecutionTypeKind.Binary64 => $"R.D({value})",
            ExecutionTypeKind.DecimalNumber => $"R.M({value})",
            ExecutionTypeKind.Text => $"R.S({value})",
            ExecutionTypeKind.Enum => IsUnsigned(((INamedTypeSymbol)type).EnumUnderlyingType!) ? $"({display})R.U({value})" : $"({display})R.I({value})",
            ExecutionTypeKind.Signed32Pair or ExecutionTypeKind.Signed32Quad => FromComponents(type, value, static item => $"checked((int)R.I({item}))"),
            ExecutionTypeKind.Binary32Pair or ExecutionTypeKind.Binary32Quad => FromComponents(type, value, static item => $"R.F({item})"),
            _ => null,
        };
    }

    /// <summary>A <c>System.Drawing</c> value built by its component constructor from the JSON array in <paramref name="value"/>.</summary>
    private static string FromComponents(ITypeSymbol type, string value, Func<string, string> component)
    {
        IEnumerable<string> components = Components(type).Select((_, i) => component($"R.At({value}, {i.ToString(CultureInfo.InvariantCulture)})"));
        return $"new {type.ToDisplayString(Qualified)}({string.Join(", ", components)})";
    }

    /// <summary>A <c>System.Drawing</c> value type's components, in its constructor's order.</summary>
    private static string[] Components(ITypeSymbol type) => type.Name switch
    {
        "Point" or "PointF" => ["X", "Y"],
        "Size" or "SizeF" => ["Width", "Height"],
        _ => ["X", "Y", "Width", "Height"],
    };

    private static string Call(IMethodSymbol method, string? receiver, List<string> arguments)
    {
        string target = receiver ?? method.ContainingType.ToDisplayString(Qualified);
        string list = string.Join(", ", arguments);
        return method switch
        {
            { MethodKind: MethodKind.Constructor } => $"new {target}({list})",
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol { IsIndexer: true } } => $"{target}[{list}]",
            { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol property } => $"{target}.{property.Name}",
            _ => $"{target}.{method.Name}({list})",
        };
    }

    /// <summary>A C# expression giving <paramref name="expression"/>'s canonical JSON text, or null when its type has none.</summary>
    private static string? Canonical(ITypeSymbol type, string expression, int depth) => type.SpecialType switch
    {
        SpecialType.System_Boolean => $"W.B({expression})",
        SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 => $"W.I({expression})",
        SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_Char => $"W.U({expression})",
        SpecialType.System_Single => $"W.F({expression})",
        SpecialType.System_Double => $"W.D({expression})",
        SpecialType.System_Decimal => $"W.M({expression})",
        SpecialType.System_String => $"W.Str({expression})",
        SpecialType.System_IntPtr => $"W.I((long){expression})",
        SpecialType.System_UIntPtr => $"W.U((ulong){expression})",
        _ => Composite(type, expression, depth),
    };

    /// <summary>
    /// An enum as its underlying integer; a one-dimensional array or a <c>List&lt;T&gt;</c> element-wise; a
    /// <c>System.Drawing</c> value type component-wise; a flat struct field by field, in declaration order (tickets P2-242
    /// and P2-246), when every field has a canonical form.
    /// </summary>
    private static string? Composite(ITypeSymbol type, string expression, int depth) => type switch
    {
        INamedTypeSymbol { EnumUnderlyingType: { } underlying } => Canonical(underlying, $"(({underlying.ToDisplayString(Qualified)}){expression})", depth),
        IArrayTypeSymbol array => array.IsSZArray ? Sequence(array.ElementType, expression, depth) : null,
        _ when IsList(type) => Sequence(((INamedTypeSymbol)type).TypeArguments[0], expression, depth),
        _ => DriverFactory.Classify(type) switch
        {
            ExecutionTypeKind.Signed32Pair or ExecutionTypeKind.Signed32Quad => Tuple(type, expression, "W.I"),
            ExecutionTypeKind.Binary32Pair or ExecutionTypeKind.Binary32Quad => Tuple(type, expression, "W.F"),
            _ when DriverStruct.Of(type) is { Comparable: true } flat => $"W.T({string.Join(", ", flat.Fields.Select(f => Canonical(f.Type, $"{expression}.{f.Name}", depth)))})",
            _ => null,
        },
    };

    private static string Tuple(ITypeSymbol type, string expression, string writer) =>
        $"W.T({string.Join(", ", Components(type).Select(c => $"{writer}({expression}.{c})"))})";

    private static string? Sequence(ITypeSymbol element, string expression, int depth)
    {
        string item = $"x{depth.ToString(CultureInfo.InvariantCulture)}";
        return Canonical(element, item, depth + 1) is { } canonical ? $"W.Seq({expression}, {item} => {canonical})" : null;
    }

    private static bool IsList(ITypeSymbol type) =>
        string.Equals(type.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.List<T>", StringComparison.Ordinal);

    private static bool IsUnsigned(INamedTypeSymbol underlying) =>
        underlying.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64;

    /// <summary>
    /// The helper classes of a driver that reaches a hidden member or reads the value builder: its type table and makers
    /// (<c>D</c>), the reflection it may use (<c>H</c>) and the value builder (<c>V</c>). A driver whose references have no
    /// <c>RuntimeHelpers.GetUninitializedObject</c> creates an instance without a constructor by <c>FormatterServices</c>;
    /// one whose references do, by <c>RuntimeHelpers</c>, which is not obsolete there.
    /// </summary>
    private static string Helpers(Shape shape)
    {
        IEnumerable<string> types = shape.Heap.Types.Select(static t => DriverHeap.TypeOf(t)!);
        IEnumerable<string> makers = shape.Heap.Makers.Select(static (m, i) => $"case {i.ToString(CultureInfo.InvariantCulture)}: return {m};");
        string uninitialized = shape.Formatter
            ? "System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type)"
            : "System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type)";
        return HelperTemplate
            .Replace("/*TYPES*/", string.Join(", ", types), StringComparison.Ordinal)
            .Replace("/*MAKERS*/", string.Join(' ', makers), StringComparison.Ordinal)
            .Replace("/*UNINITIALIZED*/", uninitialized, StringComparison.Ordinal);
    }

    /// <summary>One driver's declarations, as they are written: the receiver and arguments, and the binding of a hidden member.</summary>
    private sealed class Writer(DriverHeap heap, bool builds, bool uninitializes)
    {
        private readonly List<string> declarations = [];

        /// <summary>The declarations, after a reset of the value builder's instances when the driver has one.</summary>
        public IReadOnlyList<string> Declarations => UsesHelpers ? ["V.Reset();", .. declarations] : declarations;

        /// <summary>Whether a declaration reads the value builder or a reflective binding.</summary>
        public bool UsesHelpers { get; private set; }

        /// <summary>
        /// Declares the receiver as a new instance of <paramref name="type"/>; it takes no slot in the case line. A type the
        /// friend cannot name is constructed through its parameterless constructor, bound as a hidden member is.
        /// </summary>
        public string Construct(ITypeSymbol type)
        {
            if (DriverHeap.Nameable(type))
            {
                string display = type.ToDisplayString(Qualified);
                declarations.Add($"{display} self = new {display}();");
            }
            else
            {
                UsesHelpers = true;
                declarations.Add($"object self = H.Construct(D.Ty[{heap.Type(type).ToString(CultureInfo.InvariantCulture)}]);");
            }

            return "self";
        }

        /// <summary>Declares the argument in <paramref name="slot"/> of the case line (slot 0 is the culture); returns its name.</summary>
        public string Declare(ITypeSymbol type, int slot)
        {
            string name = $"p{(slot - 1).ToString(CultureInfo.InvariantCulture)}";
            string wire = $"a[{slot.ToString(CultureInfo.InvariantCulture)}]";
            string value = Decode(type, wire, builds) ?? (Flat(type) is { } flat ? FromFields(type.ToDisplayString(Qualified), flat, wire) : Built(type, wire));
            declarations.Add($"{Variable(type)} {name} = {value};");
            return name;
        }

        /// <summary>Declares <paramref name="name"/> as the value builder reads slot <paramref name="slot"/> against <paramref name="type"/>.</summary>
        public string Read(ITypeSymbol type, string name, int slot)
        {
            declarations.Add($"{Variable(type)} {name} = {Built(type, $"a[{slot.ToString(CultureInfo.InvariantCulture)}]")};");
            return name;
        }

        /// <summary>
        /// A flat struct built from the JSON array of its fields in <paramref name="value"/> (tickets P2-242 and P2-246): by the
        /// constructor that takes its fields when it has one, else by an object initializer, a nested struct by the same rule,
        /// and an array, a string or an object field by the value builder (ticket P2-245).
        /// </summary>
        private string FromFields(string display, DriverStruct flat, string value)
        {
            IEnumerable<string> fields = flat.Fields.Select((f, i) => Field(f.Type, $"R.At({value}, {i.ToString(CultureInfo.InvariantCulture)})"));
            return flat.Constructor is null
                ? $"new {display} {{ {string.Join(", ", flat.Fields.Zip(fields, static (f, d) => $"{f.Name} = {d}"))} }}"
                : $"new {display}({string.Join(", ", fields)})";
        }

        /// <summary>A field of a struct a driver builds, which is therefore a scalar, a buildable struct or a value builder's input.</summary>
        private string Field(ITypeSymbol type, string value) =>
            Decode(type, value, builds) ?? (Flat(type) is { } nested ? FromFields(type.ToDisplayString(Qualified), nested, value) : Built(type, value));

        /// <summary><paramref name="type"/> as a struct built in source from its fields' array, or null when the value builder reads it.</summary>
        private DriverStruct? Flat(ITypeSymbol type) =>
            uninitializes && type.Locations.Any(static l => l.IsInSource) ? null : DriverStruct.Of(type) is { Buildable: true } flat ? flat : null;

        /// <summary>
        /// The value builder's reading of <paramref name="wire"/> as <paramref name="type"/>, cast to it where it can be named.
        /// A driver whose inputs are not a replay's or a probe's has no value builder, so it has no input of that type.
        /// </summary>
        private string Built(ITypeSymbol type, string wire)
        {
            if (!builds)
            {
                throw new InvalidOperationException($"no input can be built for {type.ToDisplayString()}");
            }

            UsesHelpers = true;
            return DriverHeap.Nameable(type)
                ? $"({type.ToDisplayString(Qualified)})V.Value({wire}, typeof({type.ToDisplayString(Qualified)}))"
                : $"V.Value({wire}, D.Ty[{heap.Type(type).ToString(CultureInfo.InvariantCulture)}])";
        }

        private static string Variable(ITypeSymbol type) => DriverHeap.Nameable(type) ? type.ToDisplayString(Qualified) : "object";

        /// <summary>
        /// Binds <paramref name="method"/> once by its exact signature and returns the call through the binding: a typed
        /// delegate (an open one for an instance method) or <c>H.Call</c>, which rethrows the member's own exception.
        /// </summary>
        public string Bind(IMethodSymbol method, string? receiver, List<string> arguments, Access access)
        {
            UsesHelpers = true;
            string owner = DriverHeap.TypeOf(method.ContainingType)!;
            string parameters = string.Join(", ", method.Parameters.Select(static p => DriverHeap.TypeOf(p.Type)!));
            string bound = method.MethodKind == MethodKind.Constructor
                ? $"H.Ctor({owner}, new System.Type[] {{ {parameters} }})"
                : $"H.Method({owner}, {DriverHeap.Literal(method.MetadataName)}, {(method.IsStatic ? "true" : "false")}, new System.Type[] {{ {parameters} }})";
            if (access == Access.Invoke)
            {
                declarations.Add($"System.Reflection.MethodBase m = {bound};");
                return $"H.Call(m, {receiver ?? "null"}, new object[] {{ {string.Join(", ", arguments)} }})";
            }

            string type = DelegateType(method);
            declarations.Add($"{type} f = ({type})System.Delegate.CreateDelegate(typeof({type}), {bound});");
            return $"f({string.Join(", ", receiver is null ? arguments : [receiver, .. arguments])})";
        }

        /// <summary>The <c>Func</c> or <c>Action</c> type of an open delegate over <paramref name="method"/>.</summary>
        private static string DelegateType(IMethodSymbol method)
        {
            List<string> all = [.. method.Parameters.Select(static p => p.Type.ToDisplayString(Qualified))];
            if (DriverFactory.HasReceiver(method))
            {
                all.Insert(0, method.ContainingType.ToDisplayString(Qualified));
            }

            if (!method.ReturnsVoid)
            {
                all.Add(method.ReturnType.ToDisplayString(Qualified));
            }

            string name = method.ReturnsVoid ? "global::System.Action" : "global::System.Func";
            return all.Count == 0 ? name : $"{name}<{string.Join(", ", all)}>";
        }
    }

    private const string Template = """
        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.IO;
        using System.Text;
        using System.Threading;

        internal static class Program
        {
            private static void Main()
            {
                // The protocol keeps stdout to itself: whatever the code under test prints goes nowhere, so it can neither
                // be read as an answer nor fill the pipe the parent reads answers from (ticket P2-044).
                TextWriter protocol = new StreamWriter(Console.OpenStandardOutput());
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    string answer;
                    try
                    {
                        List<object> a = J.Parse(line);
                        string culture = (string)a[0];
                        CultureInfo info = culture == "invariant" ? CultureInfo.InvariantCulture : new CultureInfo(culture);
                        Thread.CurrentThread.CurrentCulture = info;
                        Thread.CurrentThread.CurrentUICulture = info;
                        answer = Case(a);
                    }
                    catch (Exception e)
                    {
                        answer = "[\"NotConstructible\"," + W.Str(e.GetType().FullName) + "]";
                    }

                    protocol.WriteLine(answer);
                    protocol.Flush();
                }
            }

            private static string Case(List<object> a)
            {
                /*DECLARATIONS*/
                /*RESULT*/
                try
                {
                    /*CALL*/
                }
                catch (Exception e)
                {
                    string desktop = L.Desktop(e);
                    if (desktop != null)
                    {
                        return "[\"NotConstructible\"," + W.Str(e.GetType().FullName + " loading " + desktop) + "]";
                    }

                    return "[\"Threw\"," + W.Str(e.GetType().FullName) + "]";
                }

                return /*ANSWER*/;
            }

            private static string Returned(string canonical)
            {
                return "[\"Returned\"," + canonical + "]";
            }

            private static string NotComparable(string type)
            {
                return "[\"NotComparable\"," + W.Str(type) + "]";
            }
        }

        internal static class R
        {
            public static bool B(object v) { return (bool)v; }
            public static long I(object v) { return long.Parse((string)v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }
            public static ulong U(object v) { return ulong.Parse((string)v, NumberStyles.None, CultureInfo.InvariantCulture); }
            public static float F(object v) { return BitConverter.ToSingle(BitConverter.GetBytes(uint.Parse(((string)v).Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)), 0); }
            public static double D(object v) { return BitConverter.Int64BitsToDouble(long.Parse(((string)v).Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); }
            public static decimal M(object v) { List<object> b = (List<object>)v; return new decimal(new[] { (int)I(b[0]), (int)I(b[1]), (int)I(b[2]), (int)I(b[3]) }); }
            public static string S(object v) { return (string)v; }
            public static object At(object v, int i) { return ((List<object>)v)[i]; }
        }

        internal static class W
        {
            private const string Hex = "0123456789ABCDEF";

            public static string B(bool v) { return v ? "true" : "false"; }

            public static string I(long v) { return v < 0 ? "-" + U((ulong)(-(v + 1)) + 1UL) : U((ulong)v); }

            public static string U(ulong v)
            {
                char[] c = new char[20];
                int i = 20;
                do { c[--i] = (char)('0' + (int)(v % 10)); v /= 10; } while (v != 0);
                return new string(c, i, 20 - i);
            }

            public static string F(float v) { return X(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0), 8); }

            public static string D(double v) { return X((ulong)BitConverter.DoubleToInt64Bits(v), 16); }

            public static string M(decimal v)
            {
                int[] b = decimal.GetBits(v);
                return "[" + I(b[0]) + "," + I(b[1]) + "," + I(b[2]) + "," + I(b[3]) + "]";
            }

            public static string Str(string v)
            {
                if (v == null) { return "null"; }
                StringBuilder s = new StringBuilder("\"");
                foreach (char ch in v)
                {
                    if (ch == '"' || ch == '\\') { s.Append('\\').Append(ch); }
                    else if (ch < ' ' || ch > '~') { s.Append("\\u").Append(Hex[(ch >> 12) & 15]).Append(Hex[(ch >> 8) & 15]).Append(Hex[(ch >> 4) & 15]).Append(Hex[ch & 15]); }
                    else { s.Append(ch); }
                }

                return s.Append('"').ToString();
            }

            public static string T(params string[] components) { return "[" + string.Join(",", components) + "]"; }

            public static string Seq<T>(IEnumerable<T> xs, Func<T, string> f)
            {
                if (xs == null) { return "null"; }
                StringBuilder s = new StringBuilder("[");
                bool first = true;
                foreach (T x in xs)
                {
                    if (!first) { s.Append(','); }
                    first = false;
                    s.Append(f(x));
                }

                return s.Append(']').ToString();
            }

            private static string X(ulong bits, int digits)
            {
                char[] c = new char[digits];
                for (int i = digits - 1; i >= 0; i--) { c[i] = Hex[(int)(bits & 15)]; bits >>= 4; }
                return "\"0x" + new string(c) + "\"";
            }
        }

        internal static class L
        {
            private static readonly string[] Names =
            {
                "Accessibility", "DirectWriteForwarder", "Microsoft.VisualBasic.Forms",
                "Microsoft.Win32.Registry.AccessControl", "Microsoft.Win32.SystemEvents", "PresentationCore",
                "PresentationFramework", "PresentationFramework-SystemCore", "PresentationFramework-SystemData",
                "PresentationFramework-SystemDrawing", "PresentationFramework-SystemXml",
                "PresentationFramework-SystemXmlLinq", "PresentationFramework.Aero", "PresentationFramework.Aero2",
                "PresentationFramework.AeroLite", "PresentationFramework.Classic", "PresentationFramework.Fluent",
                "PresentationFramework.Luna", "PresentationFramework.Royale", "PresentationUI", "ReachFramework",
                "System.CodeDom", "System.Configuration.ConfigurationManager", "System.Design",
                "System.Diagnostics.EventLog", "System.Diagnostics.EventLog.Messages",
                "System.Diagnostics.PerformanceCounter", "System.DirectoryServices", "System.Drawing.Common",
                "System.Drawing.Design", "System.Formats.Nrbf", "System.IO.Packaging", "System.Printing",
                "System.Private.Windows.Core", "System.Private.Windows.GdiPlus", "System.Resources.Extensions",
                "System.Security.Cryptography.Pkcs", "System.Security.Cryptography.ProtectedData",
                "System.Security.Cryptography.Xml", "System.Security.Permissions", "System.Windows.Controls.Ribbon",
                "System.Windows.Extensions", "System.Windows.Forms", "System.Windows.Forms.Design",
                "System.Windows.Forms.Design.Editors", "System.Windows.Forms.Primitives",
                "System.Windows.Input.Manipulations", "System.Windows.Presentation", "System.Windows.Primitives",
                "System.Xaml", "UIAutomationClient", "UIAutomationClientSideProviders", "UIAutomationProvider",
                "UIAutomationTypes", "WindowsFormsIntegration"
            };

            public static string Desktop(Exception e)
            {
                for (Exception x = e; x != null; x = x.InnerException)
                {
                    FileNotFoundException missing = x as FileNotFoundException;
                    FileLoadException unloadable = x as FileLoadException;
                    string file = missing != null ? missing.FileName : unloadable != null ? unloadable.FileName : null;
                    string name = file == null ? null : file.Split(',')[0].Trim();
                    if (name != null && Array.IndexOf(Names, name) >= 0)
                    {
                        return name;
                    }
                }

                return null;
            }
        }

        internal static class J
        {
            public static List<object> Parse(string s)
            {
                int i = 0;
                return (List<object>)Value(s, ref i);
            }

            private static object Value(string s, ref int i)
            {
                if (s[i] == '[')
                {
                    List<object> list = new List<object>();
                    i++;
                    if (s[i] == ']') { i++; return list; }
                    while (true)
                    {
                        list.Add(Value(s, ref i));
                        if (s[i++] == ']') { return list; }
                    }
                }

                if (s[i] == '"')
                {
                    StringBuilder b = new StringBuilder();
                    i++;
                    while (s[i] != '"')
                    {
                        char c = s[i++];
                        if (c != '\\') { b.Append(c); continue; }
                        char e = s[i++];
                        if (e == 'u') { b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)); i += 4; }
                        else { b.Append(e); }
                    }

                    i++;
                    return b.ToString();
                }

                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != ']') { i++; }
                string token = s.Substring(start, i - start);
                if (token == "null") { return null; }
                if (token == "true") { return true; }
                if (token == "false") { return false; }
                return token;
            }
        }

        /*TASKS*/
        /*HELPERS*/
        """;

    /// <summary>
    /// How a driver waits for a returned task (ticket P2-252): <c>A.Wait</c> blocks until it completes, the per-case timeout
    /// bounding the wait from the parent, and throws what its outcome is, the first inner exception of a faulted task and a
    /// <c>TaskCanceledException</c> for a cancelled one, whatever exception cancelled it; <c>A.Get</c> then gives the
    /// result. A null task throws <c>NullReferenceException</c>, as <c>await</c> on it does.
    /// </summary>
    private const string TaskTemplate = """
        internal static class A
        {
            public static void Wait(System.Threading.Tasks.Task t)
            {
                try { t.Wait(); }
                catch (AggregateException) { }
                if (t.IsCanceled) { throw new System.Threading.Tasks.TaskCanceledException(); }
                if (t.IsFaulted) { throw t.Exception.InnerExceptions[0]; }
            }

            public static T Get<T>(System.Threading.Tasks.Task<T> t)
            {
                Wait(t);
                return t.Result;
            }
        }
        """;

    /// <summary>
    /// The reflection a replay or <c>probe</c> driver may use (ADR 0062 decision 1), and nothing else: <c>H.Method</c> binds a
    /// member by name and exact parameter types, refusing a miss or an ambiguity rather than taking a nearest match, and
    /// <c>H.Ctor</c> a constructor by its exact parameter types; <c>H.Call</c> invokes either and rethrows the member's own
    /// exception; <c>H.Construct</c> calls a parameterless constructor the driver cannot name; <c>H.New</c> creates an instance without a constructor and <c>H.Set</c> writes one of its
    /// fields, <c>readonly</c> ones included. <c>V.Value</c> reads <see cref="DriverHeap"/>'s wire forms against the type
    /// being built; <c>D</c> holds the driver's type table and makers.
    /// </summary>
    private const string HelperTemplate = """
        internal static class D
        {
            internal static readonly Type[] Ty = new Type[] { /*TYPES*/ };

            internal static object Make(int k, List<object> w)
            {
                switch (k)
                {
                    /*MAKERS*/
                    default: throw new ArgumentOutOfRangeException("k");
                }
            }

            private static object Answering(List<object> w, Func<object, object> make)
            {
                return make(w.Count > 0 ? w[0] : null);
            }
        }

        internal static class H
        {
            private const System.Reflection.BindingFlags Declared = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

            public static System.Reflection.MethodInfo Method(Type owner, string name, bool isStatic, Type[] parameters)
            {
                System.Reflection.MethodInfo found = null;
                foreach (System.Reflection.MethodInfo m in owner.GetMethods(Declared | (isStatic ? System.Reflection.BindingFlags.Static : System.Reflection.BindingFlags.Instance)))
                {
                    if (m.Name != name || m.IsGenericMethodDefinition || !Same(m.GetParameters(), parameters)) { continue; }
                    if (found != null) { throw new System.Reflection.AmbiguousMatchException(owner.FullName + "::" + name); }
                    found = m;
                }

                if (found == null) { throw new MissingMethodException(owner.FullName, name); }
                return found;
            }

            public static object Call(System.Reflection.MethodBase m, object self, object[] arguments)
            {
                try
                {
                    System.Reflection.ConstructorInfo c = m as System.Reflection.ConstructorInfo;
                    return c != null ? c.Invoke(arguments) : m.Invoke(self, arguments);
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    throw;
                }
            }

            public static System.Reflection.ConstructorInfo Ctor(Type owner, Type[] parameters)
            {
                foreach (System.Reflection.ConstructorInfo c in owner.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    if (Same(c.GetParameters(), parameters)) { return c; }
                }

                throw new MissingMethodException(owner.FullName, ".ctor");
            }

            public static object Construct(Type type)
            {
                return Call(Ctor(type, Type.EmptyTypes), null, new object[0]);
            }

            public static object New(Type type)
            {
                return /*UNINITIALIZED*/;
            }

            public static void Set(object o, Type owner, string name, object value)
            {
                System.Reflection.FieldInfo f = owner.GetField(name, Declared | System.Reflection.BindingFlags.Instance);
                if (f == null) { throw new MissingFieldException(owner.FullName, name); }
                f.SetValue(o, V.Value(value, f.FieldType));
            }

            private static bool Same(System.Reflection.ParameterInfo[] p, Type[] t)
            {
                if (p.Length != t.Length) { return false; }
                for (int i = 0; i < p.Length; i++) { if (p[i].ParameterType != t[i]) { return false; } }
                return true;
            }
        }

        internal static class V
        {
            private static Dictionary<string, object> made = new Dictionary<string, object>();

            public static void Reset() { made = new Dictionary<string, object>(); }

            public static object Value(object w, Type t)
            {
                List<object> l = w as List<object>;
                string tag = l != null && l.Count > 0 ? l[0] as string : null;
                if (tag != null && tag.Length > 1 && tag[0] == '@') { return Composite(tag, l); }
                if (w == null)
                {
                    if (t.IsValueType && Nullable.GetUnderlyingType(t) == null) { throw new InvalidCastException("null is no " + t.FullName); }
                    return null;
                }

                Type u = Nullable.GetUnderlyingType(t) ?? t;
                return u.IsEnum ? Enum.ToObject(u, Scalar(w, Enum.GetUnderlyingType(u))) : Scalar(w, u);
            }

            private static object Scalar(object w, Type t)
            {
                if (t == typeof(bool)) { return (bool)w; }
                if (t == typeof(string)) { return (string)w; }
                if (t == typeof(float)) { return R.F(w); }
                if (t == typeof(double)) { return R.D(w); }
                if (t == typeof(decimal)) { return R.M(w); }
                if (t == typeof(char)) { return checked((char)R.U(w)); }
                if (t == typeof(byte)) { return checked((byte)R.U(w)); }
                if (t == typeof(ushort)) { return checked((ushort)R.U(w)); }
                if (t == typeof(uint)) { return checked((uint)R.U(w)); }
                if (t == typeof(ulong)) { return R.U(w); }
                if (t == typeof(sbyte)) { return checked((sbyte)R.I(w)); }
                if (t == typeof(short)) { return checked((short)R.I(w)); }
                if (t == typeof(int)) { return checked((int)R.I(w)); }
                if (t == typeof(long)) { return R.I(w); }
                throw new InvalidCastException("no " + t.FullName + " is built from the wire");
            }

            private static object Composite(string tag, List<object> l)
            {
                switch (tag)
                {
                    case "@r": return made[(string)l[1]];
                    case "@t": return new CancellationToken((bool)l[1]);
                    case "@c": return Keep(l[1], D.Make(int.Parse((string)l[2], CultureInfo.InvariantCulture), (List<object>)l[3]));
                    case "@a": return Elements(l);
                    case "@u": { object o = Keep(l[1], H.New(TypeAt(l[2]))); Fields(o, (List<object>)l[3]); return o; }
                    case "@s": { object o = H.New(TypeAt(l[1])); Fields(o, (List<object>)l[2]); return o; }
                    default: throw new FormatException(tag);
                }
            }

            private static object Elements(List<object> l)
            {
                Type element = TypeAt(l[2]);
                List<object> xs = (List<object>)l[3];
                Array array = Array.CreateInstance(element, xs.Count);
                Keep(l[1], array);
                for (int i = 0; i < xs.Count; i++) { array.SetValue(Value(xs[i], element), i); }
                return array;
            }

            private static void Fields(object o, List<object> fields)
            {
                foreach (object f in fields)
                {
                    List<object> x = (List<object>)f;
                    H.Set(o, TypeAt(x[0]), (string)x[1], x[2]);
                }
            }

            private static Type TypeAt(object index) { return D.Ty[int.Parse((string)index, CultureInfo.InvariantCulture)]; }

            private static object Keep(object id, object o)
            {
                if (!string.IsNullOrEmpty((string)id)) { made[(string)id] = o; }
                return o;
            }
        }
        """;
}
