using System.Globalization;

using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Verdicts;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// One side's replay inputs built from a model's heap (ADR 0062 decisions 2 and 3; ticket P2-245), in
/// <see cref="DriverHeap"/>'s wire forms. The model's state is its synthesised inputs, read here and never re-asked: a
/// <c>field.&lt;Type&gt;.&lt;Field&gt;</c> map gives a field's value per element of its declaring type's sort,
/// <c>array.*</c> and <c>length.*</c> an array's elements and length, <c>null.*</c> which elements are null, a
/// <c>cast.&lt;From&gt;.&lt;To&gt;</c> map the element a reference conversion yields and <c>istype.*</c> what a type test
/// says (VERIFICATION-MODEL section 2). An element and the elements its upcasts, and its casts whose type test passes,
/// yield are one object, of the most derived type they name; each object is built once and referred to after, so sharing
/// and cycles are the model's. A solution type's object is created without a constructor and given every field the model
/// gives any of its elements, a field the model does not give staying at its default, which the model allows. A framework
/// or third-party type is built only by its public parameterless constructor, and only when the model gives it no field. A
/// string, enum or primitive is built as replay builds it (<see cref="ReplayArguments"/>), an array from its length and
/// elements, a solution struct like an object but by value, any other struct from its fields (<see cref="DriverStruct"/>),
/// a <c>CancellationToken</c> as cancelled exactly when a model's run ends in <c>OperationCanceledException</c>, and a
/// delegate only where neither run calls through it, since the counterexample carries none of the model's answers to such a
/// call. Anything else, a window among them (ADR 0035), is not constructible, and <see cref="Reason"/> says why.
/// </summary>
internal sealed class ModelObjects
{
    private const string CancellationToken = "System.Threading.CancellationToken";

    private const int LongestArray = 1 << 16;

    /// <summary>The prefixes of the synthesised inputs the builder reads, besides <c>field.*</c>.</summary>
    private static readonly string[] Read = ["null.", "array.", "length.", "cast.", "istype.", "new."];

    private readonly Compilation compilation;

    private readonly IReadOnlyDictionary<string, IrValue> maps;

    private readonly Counterexample model;

    private readonly DriverHeap heap;

    private readonly Dictionary<string, List<(IFieldSymbol Field, IrMapValue Map)>> fields = new(StringComparer.Ordinal);

    private readonly Dictionary<IrSortValue, ITypeSymbol> reached = [];

    private readonly Dictionary<IrSortValue, IrSortValue> parent = [];

    private readonly Dictionary<IrSortValue, Instance> built = [];

    private readonly Stack<Provenance> open = new();

    private readonly List<string> failures = [];

    private ModelObjects(Compilation compilation, IReadOnlyDictionary<string, IrValue> maps, Counterexample model, DriverHeap heap)
    {
        this.compilation = compilation;
        this.maps = maps;
        this.model = model;
        this.heap = heap;
    }

    /// <summary>Why the inputs cannot be built, the first thing that failed; empty while they can.</summary>
    public string Reason => string.Concat(failures.Take(1));

    /// <summary>One built input: its wire value and how it was built (ADR 0062 decision 5).</summary>
    internal sealed record Built(string Wire, string How);

    /// <summary>An object already built: its id, what its graph holds, and how often another place referred to it.</summary>
    private sealed class Instance(string id)
    {
        public string Id { get; } = id;

        public Provenance Graph { get; set; } = new();

        public int References { get; set; }
    }

    /// <summary>What one input's graph holds: the fields it was given, the constructors it ran, and whether some object of it skipped one.</summary>
    private sealed class Provenance
    {
        public List<string> Fields { get; } = [];

        public List<string> Constructors { get; } = [];

        public bool Uninitialized { get; set; }

        public bool Delegate { get; set; }

        public void Add(Provenance other)
        {
            Fields.AddRange(other.Fields.Where(f => !Fields.Contains(f, StringComparer.Ordinal)));
            Constructors.AddRange(other.Constructors);
            Uninitialized |= other.Uninitialized;
            Delegate |= other.Delegate;
        }

        public string How => (Uninitialized, Constructors.Count, Delegate) switch
        {
            (true, _, _) when Fields.Count == 0 => "uninitialized",
            (true, _, _) => $"uninitialized {string.Join(", ", Fields)}",
            (_, > 0, _) => $"constructor {Constructors[0]}",
            (_, _, true) => "delegate",
            _ => "value",
        };
    }

    /// <summary>
    /// The builder over <paramref name="maps"/>, every synthesised input of both sides by name, for a side that resolves
    /// types in <paramref name="compilation"/>; null, with the reason, when the model constrains a static field, a field
    /// this side does not declare, or an input no driver builds (<c>typeof.*</c>, <c>caller.*</c>, <c>thrown.*</c>).
    /// </summary>
    public static (ModelObjects? Objects, string Reason) Over(Compilation compilation, IReadOnlyDictionary<string, IrValue> maps, Counterexample model, DriverHeap heap)
    {
        ModelObjects objects = new(compilation, maps, model, heap);
        List<string> constrained = [];
        foreach ((string name, IrValue value) in maps.OrderBy(static m => m.Key, StringComparer.Ordinal))
        {
            if (name.StartsWith("field.", StringComparison.Ordinal))
            {
                IrMapValue map = (IrMapValue)value;
                string sort = ((IrSort)map.MapType.Key).Name;
                if (objects.Resolve(sort, name) is { IsStatic: false, ContainingType.IsGenericType: false } field)
                {
                    objects.Add(sort, field, map);
                }
                else
                {
                    constrained.Add(name);
                }
            }
            else if (!Read.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            {
                constrained.Add(name);
            }
        }

        return constrained.Count == 0 ? (objects, string.Empty) : (null, $"the model constrains {string.Join(", ", constrained)}");
    }

    private void Add(string sort, IFieldSymbol field, IrMapValue map)
    {
        if (!fields.TryGetValue(sort, out List<(IFieldSymbol, IrMapValue)>? list))
        {
            list = [];
            fields[sort] = list;
        }

        list.Add((field, map));
    }

    /// <summary>The field <paramref name="name"/> names on the type of <paramref name="sort"/>, by the name lowering gives it (a property's backing field by the property's).</summary>
    private IFieldSymbol? Resolve(string sort, string name) =>
        Type(sort)?.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => !f.IsConst && string.Equals(name, $"field.{HeapInputs.Part(sort)}.{HeapInputs.Part((f.AssociatedSymbol as IPropertySymbol)?.Name ?? f.Name)}", StringComparison.Ordinal));

    /// <summary>The type a sort names in this side's compilation: its own assembly's first, else a unique one among its references.</summary>
    private INamedTypeSymbol? Type(string sort) => compilation.Assembly.GetTypeByMetadataName(sort) ?? compilation.GetTypeByMetadataName(sort);

    /// <summary>Walks every element <paramref name="value"/> reaches as <paramref name="type"/>, joining an element and its casts into one object.</summary>
    public void Discover(ITypeSymbol type, IrValue? value)
    {
        if (value is not IrSortValue element || IsScalar(type) || IsNull(element) || !reached.TryAdd(element, type))
        {
            return;
        }

        parent.TryAdd(element, element);
        if (type is IArrayTypeSymbol array)
        {
            foreach (IrValue item in Elements(element).Items ?? [])
            {
                Discover(array.ElementType, item);
            }

            return;
        }

        foreach ((IFieldSymbol field, IrMapValue map) in Fields(element))
        {
            Discover(field.Type, map.Read(element));
        }

        foreach ((ITypeSymbol to, IrSortValue cast) in Aliases(element, type))
        {
            Union(element, cast);
            Discover(to, cast);
        }
    }

    /// <summary>
    /// <paramref name="value"/> as an input of <paramref name="type"/> named <paramref name="name"/>, or null when it cannot
    /// be built (<see cref="Reason"/> says why).
    /// </summary>
    public Built? Input(ITypeSymbol type, IrValue? value, string name)
    {
        int depth = open.Count;
        open.Push(new Provenance());
        string? wire = Value(type, value, name);
        while (open.Count > depth + 1)
        {
            open.Pop();
        }

        Provenance root = open.Pop();
        return wire is null ? null : new Built(wire, root.How);
    }

    /// <summary>
    /// Whether the model leaves <paramref name="receiver"/>, already built, free: its object took no field, is of exactly
    /// <paramref name="type"/>, and no other input refers to it, so <c>new T()</c> is the model's state too.
    /// </summary>
    public bool Free(INamedTypeSymbol type, IrSortValue receiver)
    {
        if (type.IsValueType)
        {
            return Fields(receiver).Count == 0;
        }

        IrSortValue root = Find(receiver);
        Instance instance = built[root];
        return instance.Graph.Fields.Count == 0 && instance.References == 0 && SymbolEqualityComparer.Default.Equals(Concrete(root), type);
    }

    /// <summary>
    /// Fails when the model gives an array or a collection the bodies create (<c>new.*</c>) the reference of an input: the
    /// real creation is a fresh object, so that model's state is no real run's.
    /// </summary>
    public void CheckFresh()
    {
        foreach ((string name, IrValue value) in maps.Where(static m => m.Key.StartsWith("new.", StringComparison.Ordinal)))
        {
            IrMapValue map = (IrMapValue)value;
            if (map.Entries.Values.Append(map.Default).OfType<IrSortValue>().Any(reached.ContainsKey))
            {
                Fail($"the model's {name} is an input's reference");
            }
        }
    }

    private string? Value(ITypeSymbol type, IrValue? value, string name)
    {
        if (IsScalar(type))
        {
            return value is null ? Fail($"the model gives no value for {name}") : ReplayArguments.Wire(type, value, maps) ?? Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model");
        }

        if (string.Equals(type.ToDisplayString(), CancellationToken, StringComparison.Ordinal))
        {
            return DriverHeap.Token(Cancelled());
        }

        if (DriverFactory.IsWindow(type))
        {
            return Fail($"{name}: {DriverFactory.NeedsWindow}");
        }

        if (value is not IrSortValue element)
        {
            return Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model");
        }

        if (IsNull(element))
        {
            return type.IsValueType ? Fail($"the model's {name} is null") : "null";
        }

        return type switch
        {
            IArrayTypeSymbol { IsSZArray: true } array => Array(array, element, name),
            { IsValueType: true } => Struct(type, element, name),
            { IsReferenceType: true } => Object(element, name),
            _ => Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model"),
        };
    }

    /// <summary>An array from the model's length and elements; one with no element map has default elements, which the model allows.</summary>
    private string? Array(IArrayTypeSymbol array, IrSortValue element, string name)
    {
        if (built.TryGetValue(element, out Instance? done))
        {
            return Refer(done);
        }

        (long length, List<IrValue>? items) = Elements(element);
        if (length > LongestArray)
        {
            return Fail($"the model's array {name} is longer than {LongestArray.ToString(CultureInfo.InvariantCulture)}");
        }

        Instance instance = Begin(element);
        List<string> values = [];
        for (int i = 0; i < length; i++)
        {
            if ((items is null ? Default(array.ElementType) : Value(array.ElementType, items[i], $"an element of {name}")) is not { } wire)
            {
                return items is null ? Fail($"no default {array.ElementType.ToDisplayString()} can be built for an element of {name}") : null;
            }

            values.Add(wire);
        }

        End(instance);
        return heap.Array(instance.Id, array.ElementType, values);
    }

    /// <summary>
    /// A struct: a solution one created without a constructor and given the fields the model gives its element, any other
    /// <see cref="DriverStruct"/> from all its fields' values, the model's or, where it gives none, their defaults.
    /// </summary>
    private string? Struct(ITypeSymbol type, IrSortValue element, string name)
    {
        if (IsSolution(type) && type is INamedTypeSymbol { IsGenericType: false })
        {
            open.Peek().Uninitialized = true;
            return Written(Fields(element), element, name) is { } written ? heap.Struct(type, written) : null;
        }

        if (DriverStruct.Of(type) is not { Buildable: true } flat)
        {
            return Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model");
        }

        List<string> values = [];
        foreach (DriverStruct.Field field in flat.Fields)
        {
            IrMapValue? map = maps.GetValueOrDefault($"field.{HeapInputs.Part(element.Sort)}.{HeapInputs.Part(field.Name)}") as IrMapValue;
            if ((map is null ? Default(field.Type) : Value(field.Type, map.Read(element), $"{name}.{field.Name}")) is not { } wire)
            {
                return map is null ? Fail($"no default {field.Type.ToDisplayString()} can be built for {name}.{field.Name}") : null;
            }

            values.Add(wire);
        }

        return $"[{string.Join(',', values)}]";
    }

    /// <summary>An object of a reference type: built once per model object, then referred to.</summary>
    private string? Object(IrSortValue element, string name)
    {
        IrSortValue root = Find(element);
        if (built.TryGetValue(root, out Instance? done))
        {
            return Refer(done);
        }

        if (Concrete(root) is not { } concrete)
        {
            return Fail($"the model names no type {name} can be built as");
        }

        if (DriverFactory.IsWindow(concrete))
        {
            return Fail($"{name}: {DriverFactory.NeedsWindow}");
        }

        return concrete.TypeKind == TypeKind.Delegate
            ? Delegate(concrete, root, name)
            : IsSolution(concrete) ? Uninitialized(concrete, root, name) : Constructed(concrete, root, name);
    }

    /// <summary>A solution object created without a constructor, given every field the model gives any element of it.</summary>
    private string? Uninitialized(INamedTypeSymbol concrete, IrSortValue root, string name)
    {
        if (concrete.IsAbstract || concrete.IsGenericType)
        {
            return Fail($"{name} is of {concrete.ToDisplayString()}, which no instance is created of without a constructor");
        }

        Instance instance = Begin(root);
        open.Peek().Uninitialized = true;
        List<string> written = [];
        foreach (IrSortValue member in Members(root))
        {
            if (Written(Fields(member), member, name) is not { } some)
            {
                return null;
            }

            written.AddRange(some);
        }

        End(instance);
        return heap.Uninitialized(instance.Id, concrete, written);
    }

    /// <summary>A framework or third-party object, by its public parameterless constructor, when the model gives it no field.</summary>
    private string? Constructed(INamedTypeSymbol concrete, IrSortValue root, string name)
    {
        IMethodSymbol? constructor = ReplayArguments.PublicParameterless(concrete);
        if (constructor is null || Members(root).Any(m => Fields(m).Count > 0) || heap.Constructor(constructor) is not { } maker)
        {
            return Fail($"{name} is of {concrete.ToDisplayString()}, a type outside the solution with no public parameterless constructor the model leaves free");
        }

        Instance instance = Begin(root);
        open.Peek().Constructors.Add(constructor.ToDisplayString());
        End(instance);
        return DriverHeap.Made(instance.Id, maker, []);
    }

    /// <summary>A delegate the model's runs never call: called anyway, it throws, which no run of the model does.</summary>
    private string? Delegate(INamedTypeSymbol type, IrSortValue root, string name)
    {
        if (model.Old.Trace.Concat(model.New.Trace).Any(r => r.Arguments.OfType<IrSortValue>().Any(a => Find(a) == root)))
        {
            return Fail($"the model's runs call {name}, and the counterexample holds none of the model's answers to that call");
        }

        if (heap.Delegate(type, answers: false) is not { } maker)
        {
            return Fail($"no {type.ToDisplayString()} delegate can be built for {name}");
        }

        Instance instance = Begin(root);
        open.Peek().Delegate = true;
        End(instance);
        return DriverHeap.Made(instance.Id, maker, []);
    }

    /// <summary>Each of <paramref name="given"/>'s fields at <paramref name="element"/>, written; null when a value cannot be built.</summary>
    private List<string>? Written(List<(IFieldSymbol Field, IrMapValue Map)> given, IrSortValue element, string name)
    {
        List<string> written = [];
        foreach ((IFieldSymbol field, IrMapValue map) in given)
        {
            string label = $"{field.ContainingType.Name}.{(field.AssociatedSymbol as IPropertySymbol)?.Name ?? field.Name}";
            open.Peek().Fields.Add(label);
            if (Value(field.Type, map.Read(element), $"{name}'s {label}") is not { } wire)
            {
                return null;
            }

            written.Add(heap.Field(field, wire));
        }

        return written;
    }

    private Instance Begin(IrSortValue root)
    {
        Instance instance = new(heap.Id());
        built[root] = instance;
        open.Push(new Provenance());
        return instance;
    }

    private void End(Instance instance)
    {
        instance.Graph = open.Pop();
        open.Peek().Add(instance.Graph);
    }

    private string Refer(Instance instance)
    {
        instance.References++;
        open.Peek().Add(instance.Graph);
        return DriverHeap.Reference(instance.Id);
    }

    /// <summary>
    /// The one type an object can be built as: of the types its elements were reached as and its type tests that pass, the
    /// one that is each of them, and none a failing test names; null when there is none. Two elements of one sort are two
    /// references, so casts that join them into one object are a state no real run has, and fail.
    /// </summary>
    private INamedTypeSymbol? Concrete(IrSortValue root)
    {
        if (Members(root).GroupBy(static m => m.Sort, StringComparer.Ordinal).FirstOrDefault(static g => g.Skip(1).Any()) is { } merged)
        {
            Fail($"the model's casts make two {merged.Key} references one object");
            return null;
        }

        List<ITypeSymbol> named = [.. Members(root).Select(m => reached[m])];
        List<ITypeSymbol> refused = [];
        foreach ((ITypeSymbol to, bool passes) in Members(root).SelectMany(Tests))
        {
            (passes ? named : refused).Add(to);
        }

        INamedTypeSymbol? concrete = named.OfType<INamedTypeSymbol>()
            .Where(c => named.TrueForAll(t => Is(c, t)))
            .OrderBy(static c => c.TypeArguments.Count(static t => t is ITypeParameterSymbol))
            .FirstOrDefault();
        return concrete is not null && !refused.Exists(t => Is(concrete, t)) ? concrete : null;
    }

    /// <summary>The elements one object's casts join, in the order they were reached.</summary>
    private IEnumerable<IrSortValue> Members(IrSortValue root) => reached.Keys.Where(e => Find(e) == root);

    /// <summary>The fields the model gives elements of <paramref name="element"/>'s sort.</summary>
    private List<(IFieldSymbol Field, IrMapValue Map)> Fields(IrSortValue element) => fields.GetValueOrDefault(element.Sort) ?? [];

    /// <summary>
    /// Each element a cast of <paramref name="element"/>, a reference, yields that is the same object: an upcast's, or a
    /// cast whose type test the model passes. A cast to a type this side does not resolve fails.
    /// </summary>
    private List<(ITypeSymbol To, IrSortValue Cast)> Aliases(IrSortValue element, ITypeSymbol type)
    {
        List<(ITypeSymbol, IrSortValue)> aliases = [];
        if (type is not INamedTypeSymbol { IsReferenceType: true } named)
        {
            return aliases;
        }

        foreach ((string name, IrValue value) in maps.Where(static m => m.Key.StartsWith("cast.", StringComparison.Ordinal)))
        {
            IrMapValue map = (IrMapValue)value;
            if (map.MapType is not { Key: IrSort from, Value: IrSort to } || !string.Equals(from.Name, element.Sort, StringComparison.Ordinal))
            {
                continue;
            }

            if (Type(to.Name) is not { } target)
            {
                Fail($"the model's {name} casts {element.Sort} to {to.Name}, which this side does not resolve");
            }
            else if (Is(named, target) || Tests(element).Exists(t => t.Passes && SymbolEqualityComparer.Default.Equals(t.To.OriginalDefinition, target.OriginalDefinition)))
            {
                aliases.Add((target, (IrSortValue)map.Read(element)));
            }
        }

        return aliases;
    }

    /// <summary>
    /// Each type test the model gives <paramref name="element"/>: the type it names, resolved, and whether it passes. A test
    /// of a type this side does not resolve fails, since the object built would answer it as the model may not.
    /// </summary>
    private List<(ITypeSymbol To, bool Passes)> Tests(IrSortValue element)
    {
        string prefix = $"istype.{HeapInputs.Part(element.Sort)}.";
        List<(ITypeSymbol, bool)> tests = [];
        foreach ((string name, IrValue value) in maps.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (Tested(name[prefix.Length..]) is { } to)
            {
                tests.Add((to, ((IrBoolValue)((IrMapValue)value).Read(element)).Value));
            }
            else
            {
                Fail($"the model's {name} tests a type this side does not resolve");
            }
        }

        return tests;
    }

    /// <summary>
    /// The type an <c>istype.*</c> name's part names: its metadata name's, with a generic's arity spelled <c>`n</c> where the
    /// name spelled <c>_n</c>; else the solution type whose metadata name spells to it, such as a nested one.
    /// </summary>
    private INamedTypeSymbol? Tested(string part) =>
        Type(part) ?? Type(Arity(part)) ?? compilation.References.OfType<CompilationReference>().Select(static r => r.Compilation.Assembly).Prepend(compilation.Assembly)
            .SelectMany(static a => Types(a.GlobalNamespace))
            .FirstOrDefault(t => string.Equals(HeapInputs.Part(TypeMapper.MetadataName(t)), part, StringComparison.Ordinal));

    /// <summary><paramref name="part"/> with a trailing <c>_n</c>, a generic's arity as an input's name spells it, as <c>`n</c>.</summary>
    private static string Arity(string part)
    {
        int underscore = part.LastIndexOf('_');
        string arity = part[(underscore + 1)..];
        return arity.Length > 0 && arity.All(char.IsAsciiDigit) ? $"{part[..underscore]}`{arity}" : part;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container) =>
        container.GetMembers().OfType<INamespaceOrTypeSymbol>().SelectMany(m => m is INamedTypeSymbol type ? Types(type).Prepend(type) : Types(m));

    /// <summary>Whether a value of <paramref name="type"/> is a <paramref name="target"/>: the same type, a base, or an interface it implements.</summary>
    private static bool Is(INamedTypeSymbol type, ITypeSymbol target)
    {
        List<INamedTypeSymbol> all = [.. type.AllInterfaces];
        for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
        {
            all.Add(t);
        }

        return all.Exists(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target.OriginalDefinition));
    }

    /// <summary>
    /// An array's length, from the model's length map (0 without one, which the model allows), and its elements from its
    /// element map, or null without one: the bodies then read no element, and any value is the model's.
    /// </summary>
    private (long Length, List<IrValue>? Items) Elements(IrSortValue array)
    {
        string part = HeapInputs.Part(array.Sort);
        long length = maps.GetValueOrDefault($"length.{part}") is IrMapValue lengths ? Math.Max(0, ((IrBitVecValue)lengths.Read(array)).TwosComplement) : 0;
        IrMapValue? items = maps.GetValueOrDefault($"array.{part}") is IrMapValue all ? (IrMapValue)all.Read(array) : null;
        return length > LongestArray || items is null
            ? (length, null)
            : (length, [.. Enumerable.Range(0, (int)length).Select(i => items.Read(new IrBitVecValue(32, (ulong)i)))]);
    }

    private bool IsNull(IrSortValue element) =>
        maps.GetValueOrDefault($"null.{HeapInputs.Part(element.Sort)}") is IrMapValue map && ((IrBoolValue)map.Read(element)).Value;

    /// <summary>Whether a model run ends in cancellation: the one state a <c>CancellationToken</c> input is built cancelled for.</summary>
    private bool Cancelled() =>
        new[] { model.Old.Outcome, model.New.Outcome }.OfType<IrThrew>().Any(static t => string.Equals(t.ExceptionType, "System.OperationCanceledException", StringComparison.Ordinal));

    /// <summary>Whether <paramref name="type"/> is built as replay builds scalars: a <c>bool</c>, an integer, a <c>char</c>, an enum, a string or a number.</summary>
    private static bool IsScalar(ITypeSymbol type) => DriverFactory.Classify(type) is >= ExecutionTypeKind.Boolean and <= ExecutionTypeKind.Enum;

    /// <summary>Whether <paramref name="type"/> is declared in the solution's own source.</summary>
    private static bool IsSolution(ITypeSymbol type) => type.Locations.Any(static l => l.IsInSource);

    /// <summary>The wire value of <c>default(<paramref name="type"/>)</c> for a value the model does not give; null for a struct.</summary>
    private static string? Default(ITypeSymbol type) => DriverFactory.Classify(type) switch
    {
        ExecutionTypeKind.Boolean => "false",
        ExecutionTypeKind.Binary32 => "\"0x00000000\"",
        ExecutionTypeKind.Binary64 => "\"0x0000000000000000\"",
        ExecutionTypeKind.DecimalNumber => "[0,0,0,0]",
        ExecutionTypeKind.Text => "null",
        >= ExecutionTypeKind.Character and <= ExecutionTypeKind.Enum => "0",
        _ => type.IsReferenceType ? "null" : null,
    };

    private IrSortValue Find(IrSortValue element)
    {
        IrSortValue root = element;
        while (parent.TryGetValue(root, out IrSortValue? up) && up != root)
        {
            root = up;
        }

        return root;
    }

    /// <summary>Joins two elements into one object (<see cref="Concrete"/> refuses a join of two references of one sort).</summary>
    private void Union(IrSortValue a, IrSortValue b)
    {
        parent.TryAdd(b, b);
        parent[Find(b)] = Find(a);
    }

    private string? Fail(string reason)
    {
        failures.Add(reason);
        return null;
    }
}
