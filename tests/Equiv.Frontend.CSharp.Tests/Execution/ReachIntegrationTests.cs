using System.Collections.Immutable;
using System.Text.Json;

using Equiv.Core;
using Equiv.Core.Execution;
using Equiv.Core.Ir;
using Equiv.Core.Matching;
using Equiv.Core.Verdicts;
using Equiv.Execute;
using Equiv.Frontend.CSharp.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Execution.ReplayCompilations;

namespace Equiv.Frontend.CSharp.Tests.Execution;

/// <summary>
/// Ticket P2-245, on real drivers: a private method, a private getter, a member of a private nested type and a protected
/// member are called, their own exceptions reaching the outcome; a receiver with no public parameterless constructor is
/// built from fields; and the argument kinds ADR 0062 decision 3 adds are built. On Windows the legacy side is compiled
/// against the .NET Framework 4.8 reference assemblies and runs on .NET Framework, so both runtime kinds run every case;
/// elsewhere both sides run on the test host's .NET.
/// </summary>
public sealed class ReachIntegrationTests : IDisposable
{
    private const string Source = """
        namespace N
        {
            public class Account
            {
                private readonly string owner;
                private int balance;
                private Account(string owner) { this.owner = owner; }
                private string Label(string prefix)
                {
                    if (prefix == null) { throw new System.ArgumentNullException("prefix"); }
                    return prefix + owner + balance;
                }

                private int Balance => balance;
                protected int Twice(int x) => checked(x * 2);
                private class Ledger
                {
                    private readonly int size;
                    public Ledger(int size)
                    {
                        if (size < 0) { throw new System.ArgumentOutOfRangeException("size"); }
                        this.size = size;
                    }

                    public static int Check(int x)
                    {
                        if (x < 0) { throw new System.InvalidOperationException(); }
                        return x;
                    }

                    public int Twice() => 2 * size;
                }
            }

            public struct Packet { public byte[] Data; public string Name; public object Tag; }

            public class Node
            {
                internal Node Next;
                internal int Value;
                private Node() { }
            }

            public class Base { internal int B; }
            public class Derived : Base { private int d; private Derived() { } internal int D() { return d; } }

            public class Holder
            {
                private int[] items;
                private Node head;
                private Node other;
                private object shape;
                private Holder(int x) { }
                private int Sum()
                {
                    int sum = 0;
                    foreach (int i in items) { sum += i; }
                    Derived derived = (Derived)shape;
                    return sum + head.Next.Value + derived.D() + derived.B + (head == other ? 100 : 0) + (head.Next == head ? 1000 : 0);
                }
            }

            public struct F2 { public readonly float X, Y; public F2(float x, float y) { X = x; Y = y; } }

            public static class Kinds
            {
                public static float Sum(F2 a, F2 b) => a.X + b.Y;
                public static int Run(int[] xs, System.Threading.CancellationToken token, System.Func<int, int> f, Packet p) =>
                    xs.Length + (token.IsCancellationRequested ? 10 : 0) + (f == null ? 0 : 1000) + p.Data.Length + p.Name.Length + (p.Tag == null ? 0 : 50);
                public static int Call(System.Func<int, int> f) => f(2);
                public static int Size(Packet p) => p.Data.Length + p.Name.Length;
                public static string Cancelled(System.Threading.CancellationToken token) => token.IsCancellationRequested ? "yes" : "no";
                public static long Length(System.IO.Stream s) => s.Length;
            }
        }
        """;

    private const string NoIr = "proc \"X\" () entry B0 B0: ret";

    private static readonly TargetRuntime Self = new(TargetRuntime.RuntimeFamily.NetCore, new Version(Environment.Version.Major, Environment.Version.Minor));

    private readonly string directory = Directory.CreateTempSubdirectory("reach-integration-").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    /// <summary>Criterion 1: each hidden kind is called on both runtime kinds, its exception unwrapped, through the mechanism named.</summary>
    [Theory]
    [InlineData("N.Account", "Label", "{\"fields\":{\"owner\":\"ada\",\"balance\":3}}", "[\"to \"]", "Returned \"to ada3\"", "CreateDelegate")]
    [InlineData("N.Account", "Label", "{\"fields\":{\"owner\":\"ada\"}}", "[null]", "Threw \"System.ArgumentNullException\"", "CreateDelegate")]
    [InlineData("N.Account", "get_Balance", "{\"fields\":{\"balance\":7}}", "[]", "Returned 7", "CreateDelegate")]
    [InlineData("N.Account", "Twice", null, "[2147483647]", "Threw \"System.OverflowException\"", "CreateDelegate")]
    [InlineData("N.Account+Ledger", "Check", null, "[-1]", "Threw \"System.InvalidOperationException\"", "H.Call(m")]
    [InlineData("N.Account+Ledger", "Check", null, "[4]", "Returned 4", "H.Call(m")]
    [InlineData("N.Account+Ledger", ".ctor", null, "[-1]", "Threw \"System.ArgumentOutOfRangeException\"", "H.Ctor(")]
    [InlineData("N.Account+Ledger", "Twice", "{\"constructor\":\"int\",\"arguments\":[4]}", "[]", "Returned 8", "H.Ctor(")]
    [InlineData("N.Kinds", "Length", null, "[{\"type\":\"System.IO.MemoryStream\",\"constructor\":\"byte[]\",\"arguments\":[[1,2,3]]}]", "Returned 3", "new global::System.IO.MemoryStream(")]
    public void AHiddenMember_IsCalledOnBothRuntimesWithItsOwnException(string type, string member, string? receiver, string arguments, string outcome, string mechanism)
    {
        (ExecutionOutcome legacy, ExecutionOutcome modern, ReplayPlan plan) = Probe(type, member, receiver, arguments);

        Assert.Equal(outcome, $"{legacy.Kind} {legacy.Canonical}");
        Assert.Equal(outcome, $"{modern.Kind} {modern.Canonical}");
        Assert.Contains(mechanism, File.ReadAllText(Path.ChangeExtension(plan.Drivers!.Legacy, ".cs")), StringComparison.Ordinal);
    }

    /// <summary>Criteria 3 and 4: an array, a token, a delegate, a struct of an array, a string and an object, by probe.</summary>
    [Theory]
    [InlineData("Run", "[[1,2],\"cancelled\",{\"returns\":5},{\"Data\":[9],\"Name\":\"ab\",\"Tag\":{\"constructor\":\"\",\"arguments\":[]}}]", "Returned 1065")]
    [InlineData("Run", "[[],\"live\",null,{\"Data\":[],\"Name\":\"\",\"Tag\":null}]", "Returned 0")]
    [InlineData("Call", "[{\"returns\":5}]", "Returned 5")]
    [InlineData("Size", "[{\"Data\":[1,2,3],\"Name\":\"x\",\"Tag\":null}]", "Returned 4")]
    [InlineData("Cancelled", "[\"cancelled\"]", "Returned \"yes\"")]
    public void TheArgumentKindsDecisionThreeAdds_AreBuilt(string member, string arguments, string outcome)
    {
        (ExecutionOutcome legacy, ExecutionOutcome modern, _) = Probe("N.Kinds", member, receiver: null, arguments);

        Assert.Equal(outcome, $"{legacy.Kind} {legacy.Canonical}");
        Assert.Equal(outcome, $"{modern.Kind} {modern.Canonical}");
    }

    /// <summary>
    /// Criterion 2, on real drivers: a receiver with no public parameterless constructor built from the model's fields, an
    /// array field, a reference field shared by two fields, a cycle, and a field of <c>object</c> whose cast names
    /// <c>Derived</c>, whose base field is read through the upcast's element. Both sides run the same body, so the replay is
    /// not reproduced and its outcomes show what the drivers built: 1 + 2 + 3, 7, 9, 11, 100 for the shared node and 1000 for
    /// the cycle.
    /// </summary>
    [Fact]
    public void AReceiverFromTheModelsHeap_IsTheModelsState()
    {
        (ReplayDriverFactory factory, ProcedurePair pair, Counterexample model) = HolderReplay();

        ReplayPlan plan = factory.Create(pair, model, directory);
        ReplayResult result = Replayer().Replay(plan, model, pair.OldBody!, pair.NewBody!);

        Assert.Empty(plan.Reason);
        Assert.Equal(ReplayStatus.NotReproduced, result.Status);
        Assert.Equal("1133", result.Legacy!.Canonical);
        Assert.Equal("1133", result.Modern!.Canonical);
        Assert.Equal(
            [new ReplayInput("this", "uninitialized Holder.head, Node.Next, Node.Value, Holder.items, Holder.other, Holder.shape, Derived.d, Base.B")],
            result.Inputs!.Legacy);
    }

    /// <summary>A solution struct with <c>float</c> fields, created without a constructor from the model's field values, on both runtimes.</summary>
    [Fact]
    public void AStructFromTheModelsFloatFields_IsTheModelsState()
    {
        (CSharpCompilation legacy, CSharpCompilation modern, TargetRuntime legacyRuntime, DriverReferences installed) = Projects();
        const string ir = """
            proc "X" (%a: sort "N.F2", %b: sort "N.F2", %field.N.F2.X: map<sort "N.F2", sort "System.Single">, %field.N.F2.Y: map<sort "N.F2", sort "System.Single">)
                entry B0 B0: ret
            """;
        IrSortValue a = new("N.F2", 1), b = new("N.F2", 2);
        IrValue[] inputs =
        [
            a,
            b,
            new IrMapValue(new IrMap(new IrSort("N.F2"), new IrSort("System.Single")), IrFloat.Of(0f), ImmutableDictionary<IrValue, IrValue>.Empty.Add(a, IrFloat.Of(1.5f))),
            new IrMapValue(new IrMap(new IrSort("N.F2"), new IrSort("System.Single")), IrFloat.Of(0f), ImmutableDictionary<IrValue, IrValue>.Empty.Add(b, IrFloat.Of(-2f))),
        ];
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            legacyRuntime, Self, installed.Host, legacy, Method(legacy, "N.Kinds", "Sum"), ir, modern, Method(modern, "N.Kinds", "Sum"), ir);
        Counterexample model = new(new IrInputs([.. inputs]), new IrRun(new IrReturned(Bv(1)), [], []), new IrRun(new IrReturned(Bv(2)), [], []));

        ReplayPlan plan = factory.Create(pair, model, directory);
        ReplayResult result = Replayer().Replay(plan, model, pair.OldBody!, pair.NewBody!);

        Assert.True(plan.Reason.Length == 0, plan.Reason);
        Assert.True(result.Status == ReplayStatus.NotReproduced, $"{result.Status} {result.Reason}");
        Assert.Equal("\"0xBF000000\"", result.Legacy!.Canonical);
        Assert.Equal("\"0xBF000000\"", result.Modern!.Canonical);
    }

    private (ExecutionOutcome Legacy, ExecutionOutcome Modern, ReplayPlan Plan) Probe(string type, string member, string? receiver, string arguments)
    {
        (CSharpCompilation legacy, CSharpCompilation modern, TargetRuntime legacyRuntime, DriverReferences installed) = Projects();
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            legacyRuntime, Self, installed.Host, legacy, Method(legacy, type, member), NoIr, modern, Method(modern, type, member), NoIr);
        using JsonDocument json = JsonDocument.Parse(arguments);
        using JsonDocument? self = receiver is null ? null : JsonDocument.Parse(receiver);

        ReplayPlan plan = factory.Probe(pair, [.. json.RootElement.EnumerateArray()], self?.RootElement, directory);
        Assert.True(plan.Reason.Length == 0, plan.Reason);
        (ExecutionOutcome l, ExecutionOutcome m) = Replayer().Run(plan, Equiv.Execute.Replayer.Culture);
        return (l, m, plan);
    }

    private static (ReplayDriverFactory Factory, ProcedurePair Pair, Counterexample Model) HolderReplay()
    {
        (CSharpCompilation legacy, CSharpCompilation modern, TargetRuntime legacyRuntime, DriverReferences installed) = Projects();
        const string ir = """
            proc "X" (%array.int__: map<sort "int[]", map<bv32, bv32>>, %cast.System.Object.N.Derived: map<sort "System.Object", sort "N.Derived">,
                %cast.N.Derived.N.Base: map<sort "N.Derived", sort "N.Base">, %field.N.Base.B: map<sort "N.Base", bv32>, %field.N.Derived.d: map<sort "N.Derived", bv32>,
                %field.N.Holder.head: map<sort "N.Holder", sort "N.Node">, %field.N.Holder.items: map<sort "N.Holder", sort "int[]">,
                %field.N.Holder.other: map<sort "N.Holder", sort "N.Node">, %field.N.Holder.shape: map<sort "N.Holder", sort "System.Object">,
                %field.N.Node.Next: map<sort "N.Node", sort "N.Node">, %field.N.Node.Value: map<sort "N.Node", bv32>,
                %istype.System.Object.N.Derived: map<sort "System.Object", bool>, %length.int__: map<sort "int[]", bv32>, %this: sort "N.Holder")
                entry B0 B0: ret
            """;
        IrSortValue holder = new("N.Holder", 1), items = new("int[]", 2), node = new("N.Node", 3), shape = new("System.Object", 4), derived = new("N.Derived", 5), @base = new("N.Base", 6);
        IrValue[] inputs =
        [
            Map("int[]", Map(new IrBitVec(32), Bv(0), (Bv(0), Bv(1)), (Bv(1), Bv(2)), (Bv(2), Bv(3))), (items, null)),
            Map("System.Object", derived, (shape, derived)),
            Map("N.Derived", @base, (derived, @base)),
            Map("N.Base", Bv(11), (@base, Bv(11))),
            Map("N.Derived", Bv(9), (derived, Bv(9))),
            Map("N.Holder", node, (holder, node)),
            Map("N.Holder", items, (holder, items)),
            Map("N.Holder", node, (holder, node)),
            Map("N.Holder", shape, (holder, shape)),
            Map("N.Node", node, (node, node)),
            Map("N.Node", Bv(7), (node, Bv(7))),
            Map("System.Object", new IrBoolValue(Value: true), []),
            Map("int[]", Bv(3), (items, Bv(3))),
            holder,
        ];
        (ReplayDriverFactory factory, ProcedurePair pair) = Factory(
            legacyRuntime, Self, installed.Host, legacy, Method(legacy, "N.Holder", "Sum"), ir, modern, Method(modern, "N.Holder", "Sum"), ir);
        Counterexample model = new(new IrInputs([.. inputs]), new IrRun(new IrReturned(Bv(1)), [], []), new IrRun(new IrReturned(Bv(2)), [], []));
        return (factory, pair, model);
    }

    private static IrBitVecValue Bv(long value) => IrBitVecValue.FromSigned(32, value);

    /// <summary>A map from <paramref name="key"/>'s sort whose default and entries are given; an entry's null value is a nested map built from it.</summary>
    private static IrMapValue Map(string key, IrValue @default, params (IrValue Key, IrValue? Value)[] entries) =>
        new(new IrMap(new IrSort(key), @default.Type), @default, entries.ToImmutableDictionary(static e => e.Key, e => e.Value ?? @default));

    private static IrMapValue Map(IrType key, IrValue @default, params (IrValue Key, IrValue Value)[] entries) =>
        new(new IrMap(key, @default.Type), @default, entries.ToImmutableDictionary(static e => e.Key, static e => e.Value));

    private static (CSharpCompilation Legacy, CSharpCompilation Modern, TargetRuntime LegacyRuntime, DriverReferences Installed) Projects()
    {
        DriverReferences installed = DriverReferences.Installed();
        TargetRuntime legacyRuntime = OperatingSystem.IsWindows() ? Net48 : Self;
        IEnumerable<MetadataReference> legacyReferences = OperatingSystem.IsWindows()
            ? installed.For(Net48)!.References.Select(static r => MetadataReference.CreateFromFile(r))
            : Runtime;
        return (Project(legacyReferences), Project(Runtime), legacyRuntime, installed);
    }

    private static CSharpCompilation Project(IEnumerable<MetadataReference> references) =>
        CSharpCompilation.Create(
            "Reach",
            [CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private Replayer Replayer() => new(new ChildProcessHost(ChildProcessHost.DefaultMemoryLimitBytes).Within(directory));
}
