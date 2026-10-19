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
/// says (VERIFICATION-MODEL section 2). An element and the elements its upcasts, and its type tests that pass, yield are
/// one object, of the most derived type they name; each object is built once and referred to after, so sharing and cycles
/// are the model's. A solution type's object is created without a constructor and given every field the model gives any of
/// its elements, a field the model does not give staying at its default, which the model allows. A framework or
/// third-party type is built only by its public parameterless constructor, and only when the model gives it no field. A
/// string, enum or primitive is built as replay builds it (<see cref="ReplayArguments"/>), an array from its length and
/// elements, a solution struct like an object but by value, a framework struct from its fields (<see cref="DriverStruct"/>),
/// a <c>CancellationToken</c> as cancelled exactly when a model's run ends in <c>OperationCanceledException</c>, and a
/// delegate only where neither run calls through it, since the counterexample carries no answers of the model's for such a
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

    private readonly Counterexample? model;

    private readonly DriverHeap heap;

    private readonly Dictionary<string, List<(IFieldSymbol Field, IrMapValue Map)>> fields = new(StringComparer.Ordinal);

    private readonly Dictionary<IrSortValue, ITypeSymbol> reached = [];

    private readonly Dictionary<IrSortValue, IrSortValue> parent = [];

    private readonly Dictionary<IrSortValue, Instance> built = [];

    private readonly Stack<Provenance> open = new();

    private ModelObjects(Compilation compilation, IReadOnlyDictionary<string, IrValue> maps, Counterexample? model, DriverHeap heap)
    {
        this.compilation = compilation;
        this.maps = maps;
        this.model = model;
        this.heap = heap;
    }

    /// <summary>Why the inputs cannot be built; empty while they can.</summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>One built input: its wire value and how it was built (ADR 0062 decision 5).</summary>
    internal sealed record Built(string Wire, string How);

    /// <summary>An object already built: its id, the fields its graph was given, whether any of it skipped a constructor, and how often it was referred to.</summary>
    private sealed class Instance(string id)
    {
        public string Id { get; } = id;

        public Provenance Graph { get; set; } = new();

        public int References { get; set; }
    }

    /// <summary>What one input's graph holds: the fields it was given, and whether some object of it was created without a constructor.</summary>
    private sealed class Provenance
    {
        public List<string> Fields { get; } = [];

        public bool Uninitialized { get; set; }

        public string? Constructor { get; set; }

        public bool Delegate { get; set; }

        public void Add(Provenance other)
        {
            Fields.AddRange(other.Fields.Where(f => !Fields.Contains(f, StringComparer.Ordinal)));
            Uninitialized |= other.Uninitialized;
        }

        public string How => (Uninitialized, Constructor, Delegate) switch
        {
            (true, _, _) => Fields.Count == 0 ? "uninitialized" : $"uninitialized {string.Join(", ", Fields)}",
            (_, { } constructor, _) => $"constructor {constructor}",
            (_, _, true) => "delegate",
            _ => "value",
        };
    }

    /// <summary>
    /// The builder over <paramref name="maps"/>, every synthesised input of both sides by name, for a side that resolves
    /// types in <paramref name="compilation"/>; null, with the reason, when the model constrains a static field, a field
    /// this side does not declare, or an input no driver builds (<c>typeof.*</c>, <c>caller.*</c>, <c>thrown.*</c>).
    /// </summary>
    public static (ModelObjects? Objects, string Reason) Over(Compilation compilation, IReadOnlyDictionary<string, IrValue> maps, Counterexample? model, DriverHeap heap)
    {
        ModelObjects objects = new(compilation, maps, model, heap);
        List<string> constrained = [];
        foreach ((string name, IrValue value) in maps.OrderBy(static m => m.Key, StringComparer.Ordinal))
        {
            if (name.StartsWith("field.", StringComparison.Ordinal) && value is IrMapValue { MapType.Key: IrSort key } map)
            {
                if (objects.Resolve(key.Name, name) is { IsStatic: false, ContainingType.IsGenericType: false } field)
                {
                    objects.Add(key.Name, field, map);
                    continue;
                }

                constrained.Add(name);
            }
            else if (!IsBuilt(name))
            {
                constrained.Add(name);
            }
        }

        return constrained.Count == 0 ? (objects, string.Empty) : (null, $"the model constrains {string.Join(", ", constrained)}");
    }

    /// <summary>Whether an input of <paramref name="name"/> is one the builder reads, or one only the bodies do (<c>this</c>, <c>null.*</c>).</summary>
    private static bool IsBuilt(string name) =>
        name is IrParameterNames.Receiver
        || Read.Any(p => name.StartsWith(p, StringComparison.Ordinal));

    private void Add(string sort, IFieldSymbol field, IrMapValue map)
    {
        if (!fields.TryGetValue(sort, out List<(IFieldSymbol, IrMapValue)>? list))
        {
            list = [];
            fields[sort] = list;
        }

        list.Add((field, map));
    }

    /// <summary>The instance field <paramref name="name"/> names on the type of <paramref name="sort"/>, by the name lowering gives it (a property's backing field by the property's).</summary>
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
            foreach (IrValue item in Elements(element) is { Items: { } items } ? items : [])
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
    public bool Free(ITypeSymbol type, IrValue? receiver)
    {
        if (receiver is not IrSortValue element || !reached.ContainsKey(element))
        {
            return true;
        }

        IrSortValue root = Find(element);
        return type.IsValueType
            ? Fields(element).Count == 0
            : built.TryGetValue(root, out Instance? instance) && instance.Graph.Fields.Count == 0 && instance.References == 0
                && SymbolEqualityComparer.Default.Equals(Concrete(root), type);
    }

    /// <summary>
    /// Fails when the model gives an array or a collection the bodies create (<c>new.*</c>) the reference of an input: the
    /// real creation is a fresh object, so that model's state is no real run's.
    /// </summary>
    public void CheckFresh()
    {
        foreach ((string name, IrValue value) in maps.Where(static m => m.Key.StartsWith("new.", StringComparison.Ordinal)))
        {
            if (value is IrMapValue map && map.Entries.Values.Append(map.Default).OfType<IrSortValue>().Any(reached.ContainsKey))
            {
                Fail($"the model's {name} is an input's reference");
            }
        }
    }

    private string? Value(ITypeSymbol type, IrValue? value, string name)
    {
        IReadOnlyDictionary<string, IrValue> nullness = maps;
        return type switch
        {
            _ when IsScalar(type) => value is null ? Fail($"the model gives no value for {name}") : ReplayArguments.Wire(type, value, nullness) ?? Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model"),
            _ when string.Equals(type.ToDisplayString(), CancellationToken, StringComparison.Ordinal) => DriverHeap.Token(Cancelled()),
            _ when DriverFactory.IsWindow(type) => Fail($"{name}: {DriverFactory.NeedsWindow}"),
            _ when value is IrSortValue element && IsNull(element) => type.IsValueType ? Fail($"the model's {name} is null") : "null",
            IArrayTypeSymbol { IsSZArray: true } array => Array(array, value as IrSortValue, name),
            { IsValueType: true } => Struct(type, value as IrSortValue, name),
            { IsReferenceType: true } => value is IrSortValue element ? Object(type, element, name) : Fail($"the model gives no {type.ToDisplayString()} for {name}"),
            _ => Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model"),
        };
    }

    /// <summary>An array from the model's length and elements; one with no length map has none, which the model allows.</summary>
    private string? Array(IArrayTypeSymbol array, IrSortValue? element, string name)
    {
        if (element is null)
        {
            return Fail($"the model gives no array for {name}");
        }

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
    /// A struct: a solution one created without a constructor and given the fields the model gives its element, a
    /// <c>DriverStruct</c> from all its fields' values, the model's or their defaults; else not constructible.
    /// </summary>
    private string? Struct(ITypeSymbol type, IrSortValue? element, string name)
    {
        List<(IFieldSymbol Field, IrMapValue Map)> given = element is null ? [] : Fields(element);
        if (IsSolution(type) && type is INamedTypeSymbol { IsGenericType: false })
        {
            open.Peek().Uninitialized = true;
            return element is null ? heap.Struct(type, []) : Written(given, element, name) is { } written ? heap.Struct(type, written) : null;
        }

        if (DriverStruct.Of(type) is not { Buildable: true } flat || element is null)
        {
            return Fail($"no {type.ToDisplayString()} argument can be built for {name} from the model");
        }

        List<string> values = [];
        foreach (DriverStruct.Field field in flat.Fields)
        {
            IrMapValue? map = given.FirstOrDefault(g => string.Equals(MapName(g.Field), $"field.{HeapInputs.Part(TypeMapper.MetadataName(type))}.{HeapInputs.Part(field.Name)}", StringComparison.Ordinal)).Map;
            if ((map is null ? Default(field.Type) : Value(field.Type, map.Read(element), $"{name}.{field.Name}")) is not { } wire)
            {
                return map is null ? Fail($"no default {field.Type.ToDisplayString()} can be built for {name}.{field.Name}") : null;
            }

            values.Add(wire);
        }

        return $"[{string.Join(',', values)}]";
    }

    /// <summary>An object of a reference type: built once per model object, then referred to.</summary>
    private string? Object(ITypeSymbol type, IrSortValue element, string name)
    {
        IrSortValue root = Find(element);
        if (built.TryGetValue(root, out Instance? done))
        {
            return Refer(done);
        }

        if (Concrete(root) is not { } concrete)
        {
            return Reason.Length == 0 ? Fail($"the model names no type {name} can be built as") : null;
        }

        return concrete switch
        {
            _ when DriverFactory.IsWindow(concrete) => Fail($"{name}: {DriverFactory.NeedsWindow}"),
            { TypeKind: TypeKind.Delegate } => Delegate((INamedTypeSymbol)concrete, root, name),
            _ when IsSolution(concrete) => Uninitialized(concrete, root, name),
            _ => Constructed(concrete, root, name),
        };
    }

    /// <summary>A solution object created without a constructor, given every field the model gives any element of it.</summary>
    private string? Uninitialized(INamedTypeSymbol concrete, IrSortValue root, string name)
    {
        if (concrete.IsAbstract || concrete.TypeKind != TypeKind.Class || concrete.TypeArguments.Any(static t => t is ITypeParameterSymbol))
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
        IMethodSymbol? constructor = concrete.IsAbstract || concrete.TypeKind != TypeKind.Class
            ? null
            : concrete.InstanceConstructors.FirstOrDefault(static c => c.Parameters.IsEmpty && c.DeclaredAccessibility == Accessibility.Public);
        if (constructor is null || Members(root).Any(m => Fields(m).Count > 0) || heap.Constructor(constructor) is not { } maker)
        {
            return Fail($"{name} is of {concrete.ToDisplayString()}, a type outside the solution with no public parameterless constructor the model leaves free");
        }

        Instance instance = Begin(root);
        open.Peek().Constructor ??= constructor.ToDisplayString();
        End(instance);
        return DriverHeap.Made(instance.Id, maker, []);
    }

    /// <summary>A delegate the model's runs never call: called anyway, it throws, which no model run does.</summary>
    private string? Delegate(INamedTypeSymbol type, IrSortValue root, string name)
    {
        bool called = model is not null && model.Old.Trace.Concat(model.New.Trace).Any(r => r.Arguments.OfType<IrSortValue>().Any(a => Find(a) == root));
        if (called || heap.Delegate(type, answers: false) is not { } maker)
        {
            return Fail(called
                ? $"the model's runs call {name}, and the counterexample holds none of the model's answers to that call"
                : $"no {type.ToDisplayString()} delegate can be built for {name}");
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
            if (Value(field.Type, map.Read(element), $"{name}'s {label}") is not { } wire)
            {
                return null;
            }

            if (!open.Peek().Fields.Contains(label, StringComparer.Ordinal))
            {
                open.Peek().Fields.Add(label);
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
        Provenance graph = open.Pop();
        instance.Graph = graph;
        open.Peek().Add(graph);
        open.Peek().Constructor ??= graph.Constructor;
        open.Peek().Delegate |= graph.Delegate;
    }

    private string Refer(Instance instance)
    {
        instance.References++;
        open.Peek().Add(instance.Graph);
        return DriverHeap.Reference(instance.Id);
    }

    /// <summary>
    /// The one type an object can be built as: of the types its elements were reached as and its type tests that pass, the
    /// one that is each of them; and none a failing test names. Null when there is none.
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
        foreach (IrSortValue member in Members(root))
        {
            foreach ((ITypeSymbol to, bool passes) in Tests(member))
            {
                (passes ? named : refused).Add(to);
            }
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
    /// Each element a cast of <paramref name="element"/> yields that is the same object: an upcast's, or a cast whose type
    /// test the model passes. A cast the model's types do not resolve fails.
    /// </summary>
    private List<(ITypeSymbol To, IrSortValue Cast)> Aliases(IrSortValue element, ITypeSymbol type)
    {
        List<(ITypeSymbol, IrSortValue)> aliases = [];
        foreach ((string name, IrValue value) in maps.Where(static m => m.Key.StartsWith("cast.", StringComparison.Ordinal)))
        {
            if (value is not IrMapValue { MapType: { Key: IrSort from, Value: IrSort to } } map || !string.Equals(from.Name, element.Sort, StringComparison.Ordinal))
            {
                continue;
            }

            if (Type(to.Name) is not { } target)
            {
                Fail($"the model's {name} casts {element.Sort} to {to.Name}, which this side does not resolve");
                continue;
            }

            if (map.Read(element) is IrSortValue cast && target.IsReferenceType && type.IsReferenceType && (Is(type, target) || Passes(element, target)))
            {
                aliases.Add((target, cast));
            }
        }

        return aliases;
    }

    /// <summary>Each type test the model gives <paramref name="element"/>: the type it names, resolved, and whether it passes.</summary>
    private IEnumerable<(ITypeSymbol To, bool Passes)> Tests(IrSortValue element)
    {
        string prefix = $"istype.{HeapInputs.Part(element.Sort)}.";
        foreach ((string name, IrValue value) in maps.Where(m => m.Key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            if (value is IrMapValue { MapType.Key: IrSort key } map && string.Equals(key.Name, element.Sort, StringComparison.Ordinal) && Tested(name[prefix.Length..]) is { } to)
            {
                yield return (to, map.Read(element) is IrBoolValue { Value: true });
            }
        }
    }

    private bool Passes(IrSortValue element, ITypeSymbol target) => Tests(element).Any(t => t.Passes && SymbolEqualityComparer.Default.Equals(t.To.OriginalDefinition, target.OriginalDefinition));

    /// <summary>The type an <c>istype.*</c> name's part names: its metadata name when that has no character the name had to spell, else a solution type's whose name spells to it.</summary>
    private INamedTypeSymbol? Tested(string part) =>
        Type(part) ?? reached.Values.OfType<INamedTypeSymbol>()
            .SelectMany(static t => new[] { t }.Concat(t.AllInterfaces).Concat(Bases(t)))
            .Concat(Solution())
            .FirstOrDefault(t => string.Equals(HeapInputs.Part(TypeMapper.MetadataName(t)), part, StringComparison.Ordinal));

    /// <summary>Every type declared in the solution's source that this side's compilation sees.</summary>
    private IEnumerable<INamedTypeSymbol> Solution() =>
        compilation.References.OfType<CompilationReference>().Select(static r => r.Compilation.Assembly).Prepend(compilation.Assembly)
            .SelectMany(static a => Types(a.GlobalNamespace));

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol container) =>
        container.GetMembers().OfType<INamespaceOrTypeSymbol>().SelectMany(m => m is INamedTypeSymbol type ? Types(type).Prepend(type) : Types(m));

    private static IEnumerable<INamedTypeSymbol> Bases(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? b = type.BaseType; b is not null; b = b.BaseType)
        {
            yield return b;
        }
    }

    /// <summary>Whether a value of <paramref name="type"/> is a <paramref name="target"/>: the same type, a base, or an interface it implements.</summary>
    private static bool Is(ITypeSymbol type, ITypeSymbol target) =>
        type is INamedTypeSymbol named
            ? new[] { named }.Concat(Bases(named)).Concat(named.AllInterfaces).Any(t => SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, target.OriginalDefinition))
            : SymbolEqualityComparer.Default.Equals(type, target);

    /// <summary>
    /// An array's length, from the model's length map (0 without one, which the model allows), and its elements from its
    /// element map, or null without one: the bodies then read no element, and any value is the model's.
    /// </summary>
    private (long Length, List<IrValue>? Items) Elements(IrSortValue array)
    {
        string part = HeapInputs.Part(array.Sort);
        long length = maps.GetValueOrDefault($"length.{part}") is IrMapValue lengths && lengths.Read(array) is IrBitVecValue bits ? Math.Max(0, bits.TwosComplement) : 0;
        IrMapValue? items = maps.GetValueOrDefault($"array.{part}") is IrMapValue all ? all.Read(array) as IrMapValue : null;
        return length > LongestArray || items is null
            ? (length, null)
            : (length, [.. Enumerable.Range(0, (int)length).Select(i => items.Read(new IrBitVecValue(32, (ulong)i)))]);
    }

    private bool IsNull(IrSortValue element) =>
        maps.GetValueOrDefault($"null.{HeapInputs.Part(element.Sort)}") is IrMapValue map && map.Read(element) is IrBoolValue { Value: true };

    /// <summary>Whether a model run ends in cancellation: the one state a <c>CancellationToken</c> input is built cancelled for.</summary>
    private bool Cancelled() =>
        model is not null && new[] { model.Old.Outcome, model.New.Outcome }.OfType<IrThrew>()
            .Any(static t => t.ExceptionType is "System.OperationCanceledException" or "System.Threading.Tasks.TaskCanceledException");

    /// <summary>Whether <paramref name="type"/> is built as replay builds scalars: a <c>bool</c>, an integer, a <c>char</c>, an enum, a string or a number.</summary>
    private static bool IsScalar(ITypeSymbol type) => DriverFactory.Classify(type) is >= ExecutionTypeKind.Boolean and <= ExecutionTypeKind.Enum;

    /// <summary>Whether <paramref name="type"/> is declared in the solution's own source.</summary>
    private static bool IsSolution(ITypeSymbol type) => type.Locations.Any(static l => l.IsInSource);

    /// <summary>A field's map name, as lowering spells it.</summary>
    private static string MapName(IFieldSymbol field) =>
        $"field.{HeapInputs.Part(TypeMapper.MetadataName(field.ContainingType))}.{HeapInputs.Part((field.AssociatedSymbol as IPropertySymbol)?.Name ?? field.Name)}";

    /// <summary>The wire value of <c>default(<paramref name="type"/>)</c> for a field the model does not give; null when there is none.</summary>
    private static string? Default(ITypeSymbol type) => DriverFactory.Classify(type) switch
    {
        ExecutionTypeKind.Boolean => "false",
        ExecutionTypeKind.Binary32 => "\"0x00000000\"",
        ExecutionTypeKind.Binary64 => "\"0x0000000000000000\"",
        ExecutionTypeKind.DecimalNumber => "[0,0,0,0]",
        >= ExecutionTypeKind.Character and <= ExecutionTypeKind.Enum => "0",
        _ when type.IsReferenceType => "null",
        _ => null,
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

    /// <summary>
    /// Joins two elements into one object. Two elements of one sort are two references, so the model's casts joining them
    /// are a state no real run has, which <see cref="Concrete"/> refuses.
    /// </summary>
    private void Union(IrSortValue a, IrSortValue b)
    {
        parent.TryAdd(b, b);
        IrSortValue x = Find(a);
        IrSortValue y = Find(b);
        if (x != y)
        {
            parent[y] = x;
        }
    }

    private string? Fail(string reason)
    {
        if (Reason.Length == 0)
        {
            Reason = reason;
        }

        return null;
    }
}
