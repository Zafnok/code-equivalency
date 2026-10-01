using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>Behaviour of <c>IrLowerer</c> beyond the snapshots: opaque reasons (acceptance criterion 5) and interpreted results.</summary>
public sealed class IrLowererTests
{
    private static readonly IrBitVecValue Zero32 = Bits(32, 0);

    [Theory]
    [InlineData("static extern int M();", "M", "no-body")]
    [InlineData("static System.Collections.Generic.IEnumerable<int> M() { yield return 1; }", "M", "iterator")]
    [InlineData("static async System.Threading.Tasks.Task M(IAsyncDisposable d) { await using (d) { } }", "M", "await-using")]
    public void WholeBodyIsOneOpaque(string members, string name, string reason)
    {
        IrProcedure procedure = Method(members, name);

        IrBlock block = Assert.Single(procedure.Blocks);
        Assert.Equal(reason, Assert.Single(Opaques(procedure)).Reason);
        Assert.IsType<IrReturn>(block.Terminator);
    }

    [Theory]
    [InlineData("static extern int M();", "M")]
    [InlineData("static System.Collections.Generic.IEnumerable<int> M() { yield return 1; }", "M")]
    public void WholeBodyOpaqueIsFlagged(string members, string name) =>
        Assert.True(Assert.Single(Opaques(Method(members, name))).WholeBody);

    [Fact]
    public void AnExpressionLevelOpaqueIsNotFlagged() =>
        Assert.All(Opaques(Method("static int M(int[,] a) => a[0, 1];", "M")), static o => Assert.False(o.WholeBody));

    /// <summary>
    /// Ticket M3-025 criterion 1 (ADR 0029 decision 3): a whole-body opaque's span is the first offending construct, not the
    /// body. Lines are 1-based from <c>using System;</c>.
    /// </summary>
    [Theory]
    [InlineData("static System.Collections.Generic.IEnumerable<int> M()\n{\n    int x = 0;\n    yield return x;\n    yield break;\n}", "iterator", 7, 5, 7, 20)]
    public void WholeBodyOpaqueSpanIsTheConstructNotTheBody(string members, string reason, int startLine, int startColumn, int endLine, int endColumn)
    {
        IrOpaque opaque = Assert.Single(Opaques(Method(members)));

        Assert.Equal(reason, opaque.Reason);
        Assert.True(opaque.WholeBody);
        Assert.Equal((startLine, startColumn, endLine, endColumn), (opaque.Span.StartLine, opaque.Span.StartColumn, opaque.Span.EndLine, opaque.Span.EndColumn));
    }

    [Fact]
    public void WholeBodyOpaqueKeepsByRefParametersAsOuts()
    {
        IrProcedure procedure = Method("static extern void M(ref int a, out int b, object o);");

        IrReturn exit = Assert.IsType<IrReturn>(Assert.Single(procedure.Blocks).Terminator);
        Assert.Null(exit.Value);
        Assert.Equal(["a", "b"], exit.Outs.Select(static o => o.Final.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void AsyncTaskOfTValidates()
    {
        IrProcedure procedure = Method(
            "static async System.Threading.Tasks.Task<int> M() { await System.Threading.Tasks.Task.Delay(0); return 1; }");

        Assert.Empty(IrValidator.Validate(procedure));
    }

    [Fact]
    public void AsyncMethodNeverReportsAwaitOrMissingReturn()
    {
        IrProcedure procedure = Method("static async System.Threading.Tasks.Task M() { await System.Threading.Tasks.Task.Delay(0); }");

        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData("static int M(int[,] a) => a[0, 1];", "ArrayElementReference")]
    [InlineData("static int M(int[][] a) => a[0][1];", "ArrayElementReference")]
    [InlineData("static int M(int[] a, long i) => a[i];", "ArrayElementReference")]
    [InlineData("static int f; static void M() { System.Threading.Interlocked.Increment(ref f); }", "ref-argument")]
    [InlineData("static void M(int[] a) { System.Threading.Interlocked.Increment(ref a[0]); }", "ref-argument")]
    [InlineData("static void M(int a, Exception e) { if (a < 0) throw e; }", "Throw")]
    [InlineData("static T M<T>() where T : new() => new T();", "TypeParameterObjectCreation")]
    [InlineData("static object M<T>() => typeof(T);", "TypeOf")]
    [InlineData("static T M<T>() => default(T);", "DefaultValue")]
    [InlineData("static T M<T>() where T : struct => default(T);", "DefaultValue")]
    [InlineData("struct Point { public int X, Y; } static Point M() => default(Point);", "DefaultValue")]
    [InlineData("static C M(int[] a) => new C(ref a[0]); C(ref int x) { }", "ref-argument")]
    [InlineData("static void M(int a) { ref int r = ref a; r = 1; }", "SimpleAssignment")]
    [InlineData("static int? M(double? d) => (int?)d;", "Conversion")]
    [InlineData("static double? M(int i) => i;", "Conversion")]
    [InlineData("static int M(object o) => (int)o;", "Conversion")]
    [InlineData("static System.Collections.Generic.IEnumerable<object> M(System.Collections.Generic.IEnumerable<string> s) => s;", "Conversion")]
    [InlineData("struct S { public static implicit operator int(S s) => 0; } static int? M(S? s) => s;", "Conversion")]
    [InlineData("static bool M(object a, object b) => a == b;", "Binary")]
    [InlineData("static double? M(double? a, double? b) => a * b;", "Binary")]
    [InlineData("static double? M(double? d) => -d;", "Unary")]
    [InlineData("static decimal? M(decimal? d) => +d;", "Unary")]
    [InlineData("static int? M(int? n) => ~n;", "Unary")]
    [InlineData("static string M<T>(T t) => t?.ToString() ?? \"\";", "IsNull")]
    [InlineData("static bool? M(bool? b) => !b;", "Unary")]
    public void UnsupportedConstructIsOpaqueWithItsName(string members, string reason) =>
        Assert.Contains(Opaques(Method(members)), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));

    /// <summary>
    /// Ticket P2-025: a deconstruction of a tuple literal reads every element, converted to its target's type, before it
    /// stores any, so <c>(a, b) = (b, a)</c> swaps; declared targets are locals like any other, and a loop can step with one.
    /// </summary>
    [Theory]
    [InlineData("static int M(int a, int b) { (a, b) = (b, a); return a - b; }", 5, 7, 2)]
    [InlineData("static int M(int a, int b) { (a, b) = (a, b); return a - b; }", 5, 7, -2)]
    [InlineData("static int M(int a, int b) { long x; int y; (x, y) = (a, 3); return (int)x * y + b; }", 5, 7, 22)]
    [InlineData("static int M(int a, int b) { long x, y; (x, y) = ((long, long))(a, b); return (int)(x - y); }", 5, 7, -2)]
    [InlineData("static int M(int a, int b) { var (x, y) = (a + b, a); return x - y; }", 5, 7, 7)]
    [InlineData("static int M(int a, int b) { (int x, int y) = (b, a); return x - y; }", 5, 7, 2)]
    [InlineData("static int M(int a, int b) { int x = 0, y = 1; for (int i = 0; i < a; i++) (x, y) = (y, x + y); return x + b; }", 5, 7, 12)]
    public void DeconstructionOfATupleLiteralReadsEveryElementBeforeItStores(string members, int a, int b, int expected)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>
    /// Ticket P2-025: a deconstruction the lowering does not take apart, of a value that is not a tuple literal, into a nested
    /// tuple, a property or an array element, or whose own value is used, is one opaque with reason <c>DeconstructionAssignment</c>.
    /// </summary>
    [Theory]
    [InlineData("static int M((int, int) p) { (int x, int y) = p; return x + y; }")]
    [InlineData("static int M((int, int) p) { int x, y; (x, y) = ((int, int))p; return x + y; }")]
    [InlineData("struct S { public static implicit operator S((int, int) t) => default; public void Deconstruct(out int a, out int b) { a = 1; b = 2; } } static int M(int a) { int x, y; (x, y) = (S)(a, a); return x + y; }")]
    [InlineData("static int M(int a) { int x, y, z; (x, (y, z)) = (a, (a, a)); return x + y + z; }")]
    [InlineData("int P { get; set; } void M(int a) { (P, _) = (a, a); }")]
    [InlineData("static void M(int[] u, int a) { (u[0], u[1]) = (a, a); }")]
    [InlineData("static (int, int) M(int a, int b) => (a, b) = (b, a);")]
    public void DeconstructionItDoesNotTakeApartIsOpaque(string members) =>
        Assert.Equal("DeconstructionAssignment", Assert.Single(Opaques(Method(members)).Select(static o => o.Reason).Distinct(StringComparer.Ordinal)));

    /// <summary>
    /// Ticket P2-029 acceptance criterion 2: a call through <c>dynamic</c> is bound by the DLR at run time, so there is
    /// no callee identity to call; it stays opaque by design with reason <c>DynamicInvocation</c>.
    /// </summary>
    [Fact]
    public void ADynamicInvocationIsOpaqueByDesign() =>
        Assert.Equal("DynamicInvocation", Assert.Single(Opaques(Method("static object CallIt(dynamic d) => d.DoSomething();", "CallIt"))).Reason);

    [Theory]
    [InlineData("int M() => p;")]
    [InlineData("void M() { p = 1; }")]
    public void PrimaryConstructorParameterIsOpaque(string member)
    {
        IrProcedure procedure = Source($"class C(int p) {{ {member} }}");

        Assert.Equal("ParameterReference", Assert.Single(Opaques(procedure)).Reason);
    }

    [Theory]
    [InlineData("static int M(int a, int b) { a += b; return a; }", 5, 7, 12)]
    [InlineData("static int M(int a, int b) { a -= b; return a; }", 5, 7, -2)]
    [InlineData("static int M(int a, int b) { a *= b; return a; }", 5, 7, 35)]
    [InlineData("static int M(int a, int b) { a /= b; return a; }", 17, 5, 3)]
    [InlineData("static int M(int a, int b) { a %= b; return a; }", 17, 5, 2)]
    [InlineData("static int M(int a, int b) { a &= b; return a; }", 12, 10, 8)]
    [InlineData("static int M(int a, int b) { a |= b; return a; }", 12, 10, 14)]
    [InlineData("static int M(int a, int b) { a ^= b; return a; }", 12, 10, 6)]
    [InlineData("static int M(int a, int b) { a <<= b; return a; }", 3, 33, 6)]
    [InlineData("static int M(int a, int b) { a >>= b; return a; }", -8, 1, -4)]
    [InlineData("static int M(int a, int b) { a >>>= b; return a; }", -8, 1, int.MaxValue - 3)]
    public void CompoundAssignmentReadsOperatesAndWrites(string members, int a, int b, int expected) =>
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(Method(members), Bits(32, a), Bits(32, b)));

    /// <summary>
    /// Ticket P2-008 acceptance criterion 2: <c>s?.Length ?? 0</c> branches first on <c>s</c>'s null shadow, as
    /// <c>s == null ? 0 : s.Length</c> does, and its <c>??</c> then branches on the null shadow of the <c>int?</c> the CFG
    /// captures, not on an opaque. That second branch is the one the spelled-out form lacks (see the ticket's Deviation).
    /// </summary>
    [Fact]
    public void NullConditionalBranchesAsTheExplicitNullTestDoes()
    {
        IrProcedure conditional = Method("static int M(string s) => s?.Length ?? 0;");
        IrProcedure spelledOut = Method("static int M(string s) => s == null ? 0 : s.Length;");

        ImmutableArray<string> tests = NullTests(conditional);

        Assert.Equal("null.System.String[s]", tests[0]);
        Assert.Equal("null.System.Nullable_1", tests[1]);
        Assert.Equal("null.System.String[s]", NullTests(spelledOut)[0]);
    }

    /// <summary>Ticket P2-008: an <c>int?</c> operand of <c>??</c> is null when its null shadow says so; an unconstrained type parameter's stays opaque.</summary>
    [Fact]
    public void NullableOperandReadsItsNullShadow()
    {
        IrProcedure procedure = Method("static int M(int? n) => n ?? 0;");

        Assert.DoesNotContain(Opaques(procedure), static o => string.Equals(o.Reason, "IsNull", StringComparison.Ordinal));
        Assert.Contains(procedure.Parameters, static p => string.Equals(p.Var.Name, "null.System.Nullable_1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ticket P1-004 acceptance criterion 1: an array <c>foreach</c> is an index loop with no opaque and no call. The element
    /// is the array's slice of <c>array.int__</c> read at the array, then read at the index; the bound is <c>length.int__</c>
    /// read at the same array; the index is a bv32 phi of 0 and its step at the loop header.
    /// </summary>
    [Fact]
    public void ForEachOverAnArrayIsAnIndexLoop()
    {
        IrProcedure procedure = Method("static int M(int[] xs) { int s = 0; foreach (int x in xs) s += x; return s; }");
        ImmutableArray<IrInstruction> instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        IrVar xs = procedure.Parameters[0].Var;

        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
        IrMapRead slice = Assert.Single(instructions.OfType<IrMapRead>(), static r => r.Map.Name.StartsWith("array.int__", StringComparison.Ordinal));
        Assert.Equal(xs, slice.Key);
        IrMapRead element = Assert.Single(instructions.OfType<IrMapRead>(), r => r.Map == slice.Target);
        Assert.Contains(instructions.OfType<IrMapRead>(), r => r.Map.Name.StartsWith("length.int__", StringComparison.Ordinal) && r.Key == xs);
        IrPhi index = Assert.Single(instructions.OfType<IrPhi>(), static p => p.Target.Name.StartsWith("$index0", StringComparison.Ordinal));
        Assert.Equal(new IrBitVec(32), index.Target.Type);
        Assert.Equal(index.Target, element.Key);
    }

    /// <summary>Ticket P1-004: the element is read at the element type and converted on to the loop variable's as the CFG converts it.</summary>
    [Theory]
    [InlineData("static long M(int[] xs) { long s = 0; foreach (long x in xs) s += x; return s; }")]
    [InlineData("static object M(int[] xs) { object s = null; foreach (object x in xs) s = x; return s; }")]
    [InlineData("static object M(object[] xs) { object s = null; foreach (object x in xs) s = x; return s; }")]
    public void ForEachOverAnArrayConvertsTheElementToTheLoopVariable(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Empty(Calls(procedure));
    }

    /// <summary>Ticket P1-004: two array loops, one inside the other and after a branch on a call, each have their own index.</summary>
    [Fact]
    public void NestedArrayForEachLoopsHaveAnIndexEach()
    {
        IrProcedure procedure = Method("static bool N(int s) => s > 0; static int M(int[] a, string[] b) { int s = 0; if (N(s)) s = 1; foreach (int x in a) foreach (string y in b) s += x; return s; }");

        Assert.Empty(Opaques(procedure));
        Assert.Single(Calls(procedure));
        Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPhi>(), static p => p.Target.Name.StartsWith("$index1", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ticket P1-004 acceptance criteria 2 and 3: a loop over an array whose variable is a deconstruction, and a loop over
    /// anything but a single-dimensional array, stay the enumerator calls M4-001 lowers.
    /// </summary>
    [Theory]
    [InlineData("static int M((int, int)[] t) { int s = 0; foreach (var (p, q) in t) s += p; return s; }", "System.Collections.IEnumerator::MoveNext()")]
    [InlineData("static int M(int[,] m) { int s = 0; foreach (int x in m) s += x; return s; }", "System.Collections.IEnumerator::MoveNext()")]
    [InlineData("static int M(string t) { int s = 0; foreach (char c in t) s += c; return s; }", "System.CharEnumerator::MoveNext()")]
    public void ForEachOverAnythingElseStaysEnumeratorCalls(string members, string moveNext) =>
        Assert.Contains(Calls(Method(members)), c => string.Equals(c.Callee.Value, moveNext, StringComparison.Ordinal));

    /// <summary>A reference typed as the base, as <c>base.N()</c> is, is still the one <c>this</c> input of the containing type.</summary>
    [Fact]
    public void ABaseCallPassesThisOfTheContainingType()
    {
        IrProcedure procedure = Source("class B { public virtual int N() => 0; } class C : B { int f; public override int N() => base.N() + f; }", "N");

        IrVar receiver = Assert.Single(procedure.Parameters, static p => string.Equals(p.Var.Name, "this", StringComparison.Ordinal)).Var;
        Assert.Equal(new IrSort("C"), receiver.Type);
        Assert.Equal(receiver, Assert.Single(Assert.Single(Calls(procedure)).Args));
    }

    /// <summary>
    /// Ticket P2-031: a field declared on a base class, read through a receiver typed as a derived one, keys its map with
    /// the receiver upcast through <c>cast.&lt;Derived&gt;.&lt;Base&gt;</c>, so the key is of the map's sort.
    /// </summary>
    [Theory]
    [InlineData("class B { public int f; } class C : B { int N() => f; }", "this")]
    [InlineData("class B { public int f; } class C : B { static int N(C c) => c.f; }", "c")]
    [InlineData("class B { public int f { get; set; } } class C : B { int N() => f; }", "this")]
    [InlineData("class B { public int f; } class C : B { int N() => base.f; }", "this")]
    public void AnInheritedFieldIsReadAtTheUpcastReceiver(string source, string receiver)
    {
        IrProcedure procedure = Source(source, "N");

        IrParameter cast = Assert.Single(procedure.Parameters, static p => p.Var.Name is "cast.C.B");
        Assert.Equal(new IrMap(new IrSort("C"), new IrSort("B")), cast.Var.Type);
        IrParameter field = Assert.Single(procedure.Parameters, static p => p.Var.Name is "field.B.f");
        Assert.Equal(new IrSort("B"), ((IrMap)field.Var.Type).Key);
        IrMapRead[] reads = [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>()];
        IrMapRead upcast = Assert.Single(reads, r => r.Map == cast.Var);
        Assert.Equal(receiver, upcast.Key.Name);
        Assert.Contains(reads, r => r.Map.Name.StartsWith("field.B.f", StringComparison.Ordinal) && r.Key == upcast.Target);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>A field read through a receiver of its declaring type reads no cast map.</summary>
    [Fact]
    public void AFieldOfTheReceiversOwnTypeIsReadAtTheReceiver() =>
        Assert.DoesNotContain(
            Source("class C { int f; static int N(C c) => c.f; }", "N").Parameters,
            static p => p.Var.Name.StartsWith("cast.", StringComparison.Ordinal));

    [Fact]
    public void AStructsThisIsOpaque() =>
        Assert.Equal("InstanceReference", Assert.Single(Opaques(Source("struct C { int f; int M() => f; }"))).Reason);

    /// <summary>Ticket M4-001 acceptance criterion 3: the base constructor call, explicit or implicit, comes before the body.</summary>
    [Theory]
    [InlineData("class B { public B(int n) { } } class C : B { int f; C(int a) : base(a) { f = a; } }", "B::.ctor(int)")]
    [InlineData("class C { static int s = 1; const int K = 2; int f; C(int a) { f = a; } }", "System.Object::.ctor()")]
    public void ConstructorCallsItsBaseInitializerFirst(string source, string callee)
    {
        IrProcedure procedure = Source(source, ".ctor");

        ImmutableArray<IrInstruction> instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        Assert.Empty(Opaques(procedure));
        Assert.Equal(callee, Assert.Single(Calls(procedure)).Callee.Value);
        Assert.True(instructions.IndexOf(Calls(procedure)[0]) < instructions.IndexOf(instructions.OfType<IrMapWrite>().Single()));
    }

    /// <summary>Ticket M4-001 acceptance criterion 3: the constructor chained to runs the field initializers, not this one.</summary>
    [Fact]
    public void ConstructorChainingToThisIgnoresFieldInitializers()
    {
        IrProcedure procedure = Source("class C { int f = 1; int P { get; } = 2; C() : this(3) { } C(int a) { f = a; } }", ".ctor", parameters: 0);

        Assert.Empty(Opaques(procedure));
        Assert.Equal("C::.ctor(int)", Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>Ticket M4-008: a constructor runs the instance initializers its operation tree leaves out.</summary>
    [Theory]
    [InlineData("class C { int f = 1; C() { } }")]
    [InlineData("class C { int P { get; } = 1; C() { } }")]
    [InlineData("class C { event Action E = null; C() { } }")]
    [InlineData("class B { } class C : B { int f = 1; C() : base() { } }")]
    public void ConstructorInATypeWithFieldInitializersRunsThem(string source)
    {
        IrProcedure procedure = Source($"using System;\n{source}", ".ctor");

        Assert.Empty(Opaques(procedure));
        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>());
    }

    [Theory]
    [InlineData("static int M(int a) { a++; return a; }", 5, 6)]
    [InlineData("static int M(int a) { a--; return a; }", 5, 4)]
    [InlineData("static int M(int a) { return ++a; }", 5, 6)]
    [InlineData("static int M(int a) { return a++; }", 5, 5)]
    [InlineData("static int M(int a) { return a--; }", 5, 5)]
    [InlineData("static int M(int a) { return --a; }", 5, 4)]
    public void IncrementAndDecrementYieldTheOldValueOnlyWhenPostfix(string members, int a, int expected) =>
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(Method(members), Bits(32, a)));

    /// <summary>The operands are promoted to <c>int</c> and the result is narrowed back, as C# does.</summary>
    [Theory]
    [InlineData("static byte M(byte b) { b += 250; return b; }", 8, 10, 4)]
    [InlineData("static char M(char c) { c++; return c; }", 16, 0xFFFF, 0)]
    [InlineData("static byte M(byte b) { b <<= 1; return b; }", 8, 0x81, 2)]
    [InlineData("static long M(long a) { a <<= 65; return a; }", 64, 3, 6)]
    public void CompoundAssignmentNarrowsBackToTheTargetType(string members, int width, long a, long expected) =>
        Assert.Equal(new IrReturned(Bits(width, expected)), Run(Method(members), Bits(width, a)));

    [Theory]
    [InlineData("static byte M(byte b) { checked { b += 250; } return b; }", 8, 10, "System.OverflowException")]
    [InlineData("static byte M(byte b) { checked { b += 1; } return b; }", 8, 10, null)]
    [InlineData("static int M(int a) { checked { a += 1; } return a; }", 32, int.MaxValue, "System.OverflowException")]
    [InlineData("static int M(int a) { checked { a++; } return a; }", 32, int.MaxValue, "System.OverflowException")]
    [InlineData("static int M(int a, int b) { a /= b; return a; }", 32, 1, "System.DivideByZeroException")]
    public void CompoundAssignmentKeepsTheOperatorExceptionEdges(string members, int width, long a, string? thrown)
    {
        IrProcedure procedure = Method(members);
        IrValue[] arguments = procedure.Parameters.Length == 1 ? [Bits(width, a)] : [Bits(width, a), Bits(width, 0)];

        Assert.Equal(thrown is null ? new IrReturned(Bits(width, a + 1)) : new IrThrew(thrown), Run(procedure, arguments));
    }

    [Theory]
    [InlineData("static void M(int[] xs) { xs[0]++; }", "ArrayElementReference")]
    [InlineData("enum E { A } static void M(E e) { e += 1; }", "ParameterReference")]
    public void CompoundAssignmentToAnUnsupportedTargetIsOpaque(string members, string reason) =>
        Assert.Contains(Opaques(Method(members)), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));

    /// <summary>The CFG has no loop constructs, only back edges; the SSA builder puts phis at the header (M2-004 acceptance criterion 1).</summary>
    [Theory]
    [InlineData("static int M(int n) { int s = 0; while (n > 0) { s = s + n; n = n - 1; } return s; }")]
    [InlineData("static int M(int n) { int s = 0; for (int i = 1; i <= n; i++) s += i; return s; }")]
    [InlineData("static int M(int n) { int s = 0; int i = n; do { s += i; i--; } while (i > 0); return s; }")]
    [InlineData("static int M(int n) { int s = 0; while (true) { if (n <= 0) break; s += n; n--; } return s; }")]
    [InlineData("static int M(int n) { int s = 0; for (int i = 0; i < n; i++) { if (i == 0) continue; s += i; } return s + 3; }")]
    public void LoopsLowerWithoutOpaqueNodes(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 6)), Run(procedure, Bits(32, 3)));
    }

    [Fact]
    public void ANestedLoopKeepsBothHeadersPhis() =>
        Assert.Equal(
            new IrReturned(Bits(32, 12)),
            Run(Method("static int M(int n) { int s = 0; for (int i = 0; i < n; i++) { int k = n; while (k > 0) { s += 1; k--; } } return s + 3; }"), Bits(32, 3)));

    /// <summary>Ticket M2-004 acceptance criterion 3: the constructor call, then a throw of the static type.</summary>
    [Fact]
    public void ThrowOfANewObjectRecordsTheConstructorCallAndThrowsItsStaticType()
    {
        IrProcedure procedure = Method("class E : Exception { public E(int n) { } } static void M(int a) { if (a < 0) throw new E(a); }");

        Assert.Equal("C.E::.ctor(int)", Assert.Single(Calls(procedure)).Callee.Value);
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "C+E" });
        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData("static int M() { return Undefined(); }", "M")]
    [InlineData("static int M(int a) { int x = \"text\"; return x + a; }", "M")]
    [InlineData("static void M() { throw; }", "M")]
    [InlineData("int M => Undefined();", "get_M")]
    [InlineData("C() { Undefined(); }", ".ctor")]
    public void AnUnboundMethodLowersToOneUnboundOpaque(string members, string name)
    {
        IrProcedure procedure = Method(members, name, allowErrors: true);

        IrBlock block = Assert.Single(procedure.Blocks);
        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal("unbound", opaque.Reason);
        Assert.Equal(4, opaque.Span.StartLine);
        Assert.IsType<IrReturn>(block.Terminator);
    }

    [Fact]
    public void EveryErrorInAnUnboundMethodIsACause()
    {
        IrProcedure procedure = Method("static int M(int a) {\n int x = Undefined();\n int y = Missing();\n return a; }", allowErrors: true);

        ImmutableArray<IrOpaque> opaques = Opaques(procedure);
        Assert.Equal([5, 6], opaques.Select(static o => o.Span.StartLine));
        Assert.All(opaques, static o => Assert.Equal("unbound", o.Reason));
        Assert.Null(opaques[0].Target);
        Assert.NotNull(opaques[1].Target);
    }

    [Fact]
    public void AMethodReadingAnErrorTypedFieldIsUnboundAtTheRead()
    {
        // The error is on the field's declaration, outside M; M's own body binds without a diagnostic.
        IrProcedure procedure = Method("Missing f;\nobject M() {\n return f; }", allowErrors: true);

        IrOpaque opaque = Assert.Single(Opaques(procedure));
        Assert.Equal("unbound", opaque.Reason);
        Assert.Equal(6, opaque.Span.StartLine);
    }

    [Fact]
    public void RethrowIsOpaque() =>
        Assert.Equal("rethrow", Assert.Single(Opaques(ErroneousBody("static void M() { throw; }"))).Reason);

    [Fact]
    public void ObjectCreationIsACallToTheConstructor()
    {
        IrCall call = Assert.Single(Calls(Method("static C M(int a) => new C(a); C(int x) { }")));

        Assert.Equal("C::.ctor(int)", call.Callee.Value);
        Assert.Equal(["a"], call.Args.Select(static v => v.Name), StringComparer.Ordinal);
    }

    /// <summary>Ticket M2-004 acceptance criterion 2: the CFG's chain of equality tests folds back into one switch.</summary>
    [Theory]
    [InlineData("static int M(int n) { switch (n) { case 1: return 10; case 3: return 30; default: return 0; } }")]
    [InlineData("static int M(int n) => n switch { 1 => 10, 3 => 30, _ => 0 };")]
    [InlineData("static int M(int n) { switch (n) { case 1: case 3: break; default: return 0; } return n * 10; }")]
    public void ASwitchOnAnIntegralScrutineeLowersToOneSwitchTerminator(string members)
    {
        IrProcedure procedure = Method(members);

        IrSwitch terminator = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrSwitch>());
        Assert.Equal([Bits(32, 1), Bits(32, 3)], terminator.Cases.Select(static c => c.Value));
        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 30)), Run(procedure, Bits(32, 3)));
        Assert.Equal(new IrReturned(Bits(32, 0)), Run(procedure, Bits(32, 2)));
    }

    [Fact]
    public void ASwitchOnABoolScrutineeLowersToOneSwitchTerminator()
    {
        IrProcedure procedure = Method("static int M(bool b, bool c) { switch (b) { case true: return 1; default: break; } switch (c) { case false: return 2; case true: return 3; } }");

        IrSwitch terminator = Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrSwitch>());
        Assert.Equal([new IrBoolValue(Value: false), new IrBoolValue(Value: true)], terminator.Cases.Select(static c => c.Value));
        Assert.Equal(new IrReturned(Bits(32, 2)), Run(procedure, new IrBoolValue(Value: false), new IrBoolValue(Value: false)));
    }

    /// <summary>A single equality test is not a chain, so `if` keeps its branch.</summary>
    [Fact]
    public void ASingleCaseSwitchStaysABranch() =>
        Assert.Empty(Method("static int M(int n) { switch (n) { case 1: return 2; } return 0; }")
            .Blocks.Select(static b => b.Terminator).OfType<IrSwitch>());

    [Theory]
    [InlineData("static int M(int n) => n switch { > 1 => 2, _ => 0 };")]
    [InlineData("static int M(object o) => o switch { int n => n, _ => 0 };")]
    public void APatternBeyondAConstantIsOpaque(string members) =>
        Assert.Contains(Opaques(Method(members)), static o => string.Equals(o.Reason, "switch-pattern", StringComparison.Ordinal));

    [Fact]
    public void AConstantPatternOutsideASwitchIsAnEquality() =>
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), Run(Method("static bool M(int n) => n is 5;"), Bits(32, 5)));

    /// <summary>A guard is an ordinary branch after the constant test, so the arm still lowers.</summary>
    [Fact]
    public void AGuardedConstantPatternLowersAsABranch()
    {
        IrProcedure procedure = Method("static int M(int n, bool c) => n switch { 1 when c => 2, _ => 0 };");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 2)), Run(procedure, Bits(32, 1), new IrBoolValue(Value: true)));
        Assert.Equal(new IrReturned(Bits(32, 0)), Run(procedure, Bits(32, 1), new IrBoolValue(Value: false)));
    }

    /// <summary>Ticket M2-004 acceptance criterion 5: a reference parameter starts with an unconstrained shadow.</summary>
    [Fact]
    public void AReferenceParameterGetsAnUnconstrainedNullShadow()
    {
        IrProcedure procedure = Method("static bool M(string s) => s == null;");

        IrParameter nulls = Assert.Single(procedure.Parameters, static p => p.Var.Name is "null.System.String");
        Assert.Equal(new IrMap(new IrSort("System.String"), new IrBool()), nulls.Var.Type);
        Assert.Equal(IrParameterKind.In, nulls.Kind);
        Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>(), r => r.Map == nulls.Var);
        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData("static bool M(string s) => s == null;", true, true)]
    [InlineData("static bool M(string s) => s != null;", true, false)]
    [InlineData("static bool M(string s) => null == s;", false, false)]
    public void AComparisonWithNullReadsTheShadow(string members, bool isNull, bool expected)
    {
        IrProcedure procedure = Method(members);

        Assert.Equal(new IrReturned(new IrBoolValue(expected)), Run(procedure, Reference(0), Nulls("System.String", 0, isNull)));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void DereferencingAPossiblyNullReceiverThrowsNullReferenceException(bool isNull, bool thrown)
    {
        IrProcedure procedure = Method("static int M(string s) => s.CompareTo(s);");

        IrOutcome outcome = Run(procedure, Reference(0), Nulls("System.String", 0, isNull));

        // A false shadow leaves only the call's own threw edge, which is a different exception type.
        Assert.Equal(thrown, outcome is IrThrew { ExceptionType: "System.NullReferenceException" });
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>A `new` is proven non-null, so neither the shadow nor the dereference branch is emitted.</summary>
    [Fact]
    public void ANewObjectNeedsNoNullCheck()
    {
        IrProcedure procedure = Method("void F() { } static void M() { new C().F(); }");

        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>A local takes the nullness of what was stored into it.</summary>
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 7)]
    public void ALocalCarriesTheShadowOfWhatWasAssignedToIt(bool isNull, int expected)
    {
        IrProcedure procedure = Method("static int M(string s) { string t = s; if (t == null) return 0; return 7; }");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Reference(0), Nulls("System.String", 0, isNull)));
    }

    /// <summary>A value with no shadow of its own asks the `null.&lt;Sort&gt;` map, so equal references are equally null.</summary>
    [Fact]
    public void AValueWithoutAShadowTakesItsNullnessFromTheMap()
    {
        IrProcedure procedure = Method("static C F() => null; static void M() { F().G(); } void G() { }");

        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Single(procedure.Parameters, static p => p.Var.Name is "null.C");
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>A constant of an uninterpreted sort is a designated element, so equal constants are equal values.</summary>
    [Fact]
    public void ConstantsOfAnUninterpretedSortAreDesignatedElements()
    {
        List<IrValue> constants =
            [.. Method("static string M(bool b) { if (b) return null; return \"hello\"; }")
                .Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().Select(static c => c.Value)];

        Assert.Contains(constants, static c => c is IrSortValue { Sort: "System.String", Id: 0 });
        Assert.Contains(constants, static c => c is IrSortValue { Sort: "System.String", Id: not 0 });
    }

    [Fact]
    public void ALocalAssignedTheNullLiteralIsNull()
    {
        IrProcedure procedure = Method("static bool M() { string s = null; return s == null; }");

        Assert.Empty(procedure.Parameters);
        Assert.Equal(new IrReturned(new IrBoolValue(Value: true)), Run(procedure));
    }

    /// <summary>
    /// Ticket P2-003 acceptance criterion 2: a type parameter's <c>default</c> is the null element of its sort when the
    /// parameter is constrained to <c>class</c>; Roslyn folds it to the constant <c>null</c>, so it needs no dedicated
    /// lowering arm, the same path a closed reference type's <c>default</c> already took.
    /// </summary>
    [Fact]
    public void DefaultOfAClassConstrainedTypeParameterIsTheNullElement()
    {
        IrProcedure procedure = Method("static T M<T>() where T : class => default(T);");

        Assert.Empty(Opaques(procedure));
        IrConst value = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>());
        Assert.Equal(new IrSortValue("T", 0), value.Value);
    }

    [Fact]
    public void ALocalAssignedANewObjectIsNeverNull()
    {
        IrProcedure procedure = Method("void F() { } static void M() { C c = new C(); c.F(); }");

        Assert.Empty(procedure.Parameters);
        Assert.NotEqual(new IrThrew("System.NullReferenceException"), Run(procedure));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void TheReceiverOfAnInstanceMethodIsTheThisInput()
    {
        IrProcedure procedure = Method("int F() => 1; int M() => F();");

        IrParameter receiver = Assert.Single(procedure.Parameters, static p => p.Var.Name is "this");
        Assert.Equal(new IrSort("C"), receiver.Var.Type);
        Assert.Equal([receiver.Var], Assert.Single(Calls(procedure)).Args);
    }

    /// <summary>Ticket P2-004 acceptance criterion 1: a field, instance or static, is a compound target: its map is read and written once.</summary>
    [Theory]
    [InlineData("int f; static void M(C c, int a) { c.f += a; }")]
    [InlineData("int f; void M() { f++; }")]
    [InlineData("static int f; static void M() { --f; }")]
    public void AFieldIsACompoundTarget(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>(), static r => r.Map.Name is "field.C.f");
        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>());
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket P2-004: inside its declaring type, a field-like event is read as its backing field, at the receiver.</summary>
    [Theory]
    [InlineData("event Action? E; static Action? M(C c) => c.E;")]
    [InlineData("static event Action? E; static Action? M() => E;")]
    public void AFieldLikeEventReadInItsTypeIsItsFieldMap(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Single(procedure.Parameters, static p => p.Var.Name is "field.C.E");
        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>(), static r => r.Map.Name.StartsWith("field.C.E", StringComparison.Ordinal));
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket P2-004 acceptance criterion 2: a field-like event read from a nested type stays opaque.</summary>
    [Fact]
    public void AFieldLikeEventReadOutsideItsTypeIsOpaque()
    {
        Compilation compilation = RoslynTestCompilations.Compile("using System;\nclass D { public event Action? E; public class C { static Action? M(D d) => d.E; } }\n");
        IMethodSymbol method = compilation.GetTypeByMetadataName("D+C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);

        Assert.Equal("EventReference", Assert.Single(Opaques(procedure)).Reason);
    }

    /// <summary>
    /// Ticket P2-005 acceptance criterion 1: <c>+=</c> and <c>-=</c> call the event's <c>add</c> or <c>remove</c> accessor with
    /// the receiver, when there is one, then the handler, and read no field map, whether the event is field-like or has
    /// explicit accessors (which, by CS0079, is its only use: ticket P2-004 acceptance criterion 2).
    /// </summary>
    [Theory]
    [InlineData("event Action? E; static void M(C c, Action h) { c.E += h; }", "C::add_E(System.Action)", new[] { "c", "h" })]
    [InlineData("event Action? E; void M(Action h) { E -= h; }", "C::remove_E(System.Action)", new[] { "this", "h" })]
    [InlineData("static event Action? E; static void M(Action h) { E += h; }", "C::add_E(System.Action)", new[] { "h" })]
    [InlineData("event Action E { add { } remove { } } void M(Action h) { E += h; }", "C::add_E(System.Action)", new[] { "this", "h" })]
    public void AnEventAssignmentCallsItsAccessor(string members, string accessor, string[] arguments)
    {
        IrProcedure procedure = Method(members);

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal(accessor, call.Callee.Value);
        Assert.Null(call.Target);
        Assert.Equal(arguments, call.Args.Select(static a => a.Name), StringComparer.Ordinal);
        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("field.", StringComparison.Ordinal));
    }

    /// <summary>Ticket P2-005: <c>+=</c> on a null receiver throws <c>NullReferenceException</c> at the call, as a setter does.</summary>
    [Fact]
    public void AnEventAssignmentNullChecksItsReceiver()
    {
        IrProcedure procedure = Method("event Action? E; static void M(C o, Action h) { o.E += h; }");

        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(procedure, [.. procedure.Parameters.Select(p => NullTarget(p.Var, 0))]));
    }

    /// <summary>Ticket P2-005 acceptance criterion 2: a lambda handler keeps its own <c>DelegateCreation</c> reason; the call is still lowered.</summary>
    [Fact]
    public void ALambdaHandlerKeepsItsOwnReason()
    {
        IrProcedure procedure = Method("event Action? E; void M() { E += () => { }; }");

        Assert.Equal("DelegateCreation", Assert.Single(Opaques(procedure)).Reason);
        Assert.Equal("C::add_E(System.Action)", Assert.Single(Calls(procedure)).Callee.Value);
    }

    /// <summary>Ticket M2-004 acceptance criterion 6: a field is one SSA map keyed by its receiver.</summary>
    [Fact]
    public void AnInstanceFieldIsAMapKeyedByTheReceiver()
    {
        IrProcedure procedure = Method("int f; static int M(C a, C b) { a.f = 1; b.f = 2; return a.f; }");

        IrParameter map = Assert.Single(procedure.Parameters, static p => p.Var.Name is "field.C.f");
        Assert.Equal(new IrMap(new IrSort("C"), new IrBitVec(32)), map.Var.Type);
        Assert.Equal(2, procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>().Count());
        Assert.Empty(Opaques(procedure));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(0, 0, 2)]
    public void AFieldWriteIsVisibleToALaterReadOfTheSameReceiver(int a, int b, int expected) =>
        Assert.Equal(
            new IrReturned(Bits(32, expected)),
            Run(
                Method("int f; static int M(C x, C y) { x.f = 1; y.f = 2; return x.f; }"),
                Reference(a, "C"),
                Reference(b, "C"),
                Fields("C", new IrBitVec(32)),
                Nulls("C", a, isNull: false)));

    [Fact]
    public void AStaticFieldIsTheSameMapKeyedByItsTypeToken()
    {
        IrProcedure procedure = Method("static int f; static int M() { f = 5; return f; }");

        Assert.Single(procedure.Parameters, static p => p.Var.Name is "field.C.f");
        Assert.Equal(new IrSortValue("C", 0), Assert.IsType<IrConst>(procedure.Blocks[1].Instructions[0]).Value);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>
    /// Ticket P2-007: the CFG flow-captures an assignment's target ahead of a value that branches (a conditional
    /// expression, here), so a field, not only a local or an auto-property (ticket M4-008), must still resolve through
    /// the captured reference to its map.
    /// </summary>
    [Fact]
    public void AFieldTargetCapturedAheadOfABranchingValueIsStillItsMap()
    {
        IrProcedure procedure = Method("int f; static void M(C a, bool cond) { a.f = cond ? 1 : 2; }");

        Assert.Empty(Opaques(procedure));
        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapWrite>());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    public void ACapturedFieldTargetWritesTheBranchedValue(bool cond, int expected) =>
        Assert.Equal(
            new IrReturned(Bits(32, expected)),
            Run(
                Method("int f; static int M(C a, bool cond) { a.f = cond ? 1 : 2; return a.f; }"),
                Reference(0, "C"),
                new IrBoolValue(cond),
                Fields("C", new IrBitVec(32)),
                Nulls("C", 0, isNull: false)));

    /// <summary>A field of a struct-typed field is the same captured-target case, one level of receiver deeper.</summary>
    [Fact]
    public void AFieldOfAStructTypedFieldCapturedAheadOfABranchingValueIsStillItsMap()
    {
        IrProcedure procedure = Method(
            "struct P { public int X; } P p; static void M(C a, bool cond) { a.p.X = cond ? 1 : 2; }");

        Assert.Empty(Opaques(procedure));
    }

    /// <summary>An array-typed field captured the same way is still its map, whichever array the branch picks.</summary>
    [Fact]
    public void AnArrayTypedFieldCapturedAheadOfABranchingValueIsStillItsMap()
    {
        IrProcedure procedure = Method("int[] data; static void M(C a, bool cond) { a.data = cond ? new int[4] : new int[2]; }");

        Assert.Empty(Opaques(procedure));
    }

    /// <summary>A compound assignment's target is flow-captured the same way when its value branches.</summary>
    [Fact]
    public void ACompoundAssignmentToAFieldTargetCapturedAheadOfABranchingValueIsStillItsMap()
    {
        IrProcedure procedure = Method("int f; static void M(C a, bool cond) { a.f += cond ? 1 : 2; }");

        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket P1-006 acceptance criterion 1: one element map and one length map per array sort, keyed by the array.</summary>
    [Fact]
    public void AnArrayElementIsAMapKeyedByTheArrayThenTheIndex()
    {
        IrProcedure procedure = Method("static int M(int[] a, int i) { a[i] = 1; return a[0] + a.Length; }");

        IrSort array = new("int[]");
        Assert.Single(procedure.Parameters, p => p.Var.Name is "array.int__" && p.Var.Type == new IrMap(array, new IrMap(new IrBitVec(32), new IrBitVec(32))));
        Assert.Single(procedure.Parameters, p => p.Var.Name is "length.int__" && p.Var.Type == new IrMap(array, new IrBitVec(32)));
        Assert.Contains(procedure.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.IndexOutOfRangeException" });
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>
    /// Ticket P1-006 acceptance criteria 2 and 3: both writes and the read go through the one element map, read and
    /// written at the array reference, so passing one array as both parameters makes the second write the one read.
    /// </summary>
    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(1, 1, 2)]
    public void Aliased_ArrayParameters_AreNotDisjoint(int a, int b, int expected)
    {
        IrProcedure procedure = Method("static int M(int[] a, int[] b) { a[0] = 1; b[0] = 2; return a[0]; }");

        IrVar elements = Assert.Single(procedure.Parameters, static p => p.Var.Name.StartsWith("array.", StringComparison.Ordinal)).Var;
        ImmutableArray<IrInstruction> instructions = [.. procedure.Blocks.SelectMany(static b => b.Instructions)];
        ImmutableArray<IrMapWrite> slices = [.. instructions.OfType<IrMapWrite>().Where(w => w.Target.Type == elements.Type)];
        Assert.Equal(["a", "b"], slices.Select(static w => w.Key.Name), StringComparer.Ordinal);
        Assert.Equal(elements, slices[0].Map);
        Assert.Equal(slices[0].Target, slices[1].Map);
        Assert.Contains(instructions, i => i is IrMapRead read && read.Map == slices[1].Target && read.Key.Name is "a");
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.EndsWith(".a", StringComparison.Ordinal) || p.Var.Name.EndsWith(".b", StringComparison.Ordinal));

        IrOutcome outcome = Run(
            procedure,
            Reference(a, "int[]"),
            Reference(b, "int[]"),
            Elements("int[]", new IrBitVec(32)),
            Lengths("int[]", 1),
            Nulls("int[]", 0, isNull: true));

        Assert.Equal(new IrReturned(Bits(32, expected)), outcome);
    }

    /// <summary>Ticket P2-001 acceptance criterion 1: neither repro method is opaque anywhere.</summary>
    [Theory]
    [InlineData("static int[] M(int a, int b) { var r = new int[2]; r[0] = a; r[1] = b; return r; }")]
    [InlineData("static int M(int n) => new[] { n, n + 1 }[0];")]
    public void AnArrayCreationIsNotOpaque(string members) => Assert.Empty(Opaques(Method(members)));

    /// <summary>
    /// Ticket P2-001 acceptance criterion 2: a negative length throws <c>OverflowException</c> before anything is allocated;
    /// any other length gives an array of that length whose elements are the default.
    /// </summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(int.MinValue, true)]
    [InlineData(0, false)]
    [InlineData(3, false)]
    public void ANegativeLengthThrowsOverflow(int n, bool thrown)
    {
        IrProcedure procedure = Method("static int M(int n) { var r = new int[n]; return r.Length; }");

        IrOutcome outcome = Run(procedure, [Bits(32, n), .. procedure.Parameters.Skip(1).Select(static p => Input(p.Var))]);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(thrown ? new IrThrew("System.OverflowException") : new IrReturned(Bits(32, n)), outcome);
    }

    /// <summary>
    /// Ticket P2-001: a new array's elements are the element type's default, or the initialiser's values in order, and two
    /// creations are two arrays; a body that creates one writes its length map, so the map is <c>Ref</c>.
    /// </summary>
    [Theory]
    [InlineData("static int M(int n) { var r = new int[2]; return r[1]; }", 0)]
    [InlineData("static int M(int n) => new[] { n, n + 1 }[1];", 6)]
    [InlineData("static int M(int n) { var a = new int[1]; var b = new int[1]; a[0] = n; b[0] = 2; return a[0]; }", 5)]
    [InlineData("static int M(int n) { var a = new int[] { n }; var b = new int[3]; return a.Length * 10 + b.Length; }", 13)]
    public void ANewArrayHoldsItsDefaultsOrItsInitialiser(string members, int expected)
    {
        IrProcedure procedure = Method(members);

        IrOutcome outcome = Run(procedure, [Bits(32, 5), .. procedure.Parameters.Skip(1).Select(static p => Input(p.Var))]);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), outcome);
        Assert.All(procedure.Parameters.Where(static p => p.Var.Name.StartsWith("length.", StringComparison.Ordinal)), static p => Assert.Equal(IrParameterKind.Ref, p.Kind));
    }

    /// <summary>Ticket P2-001 acceptance criterion 3: several dimensions, a jagged creation and a <c>long</c> length stay opaque.</summary>
    [Theory]
    [InlineData("static int[,] M(int n) => new int[n, 2];")]
    [InlineData("static int[][] M(int n) => new int[n][];")]
    [InlineData("static int[] M(long n) => new int[n];")]
    [InlineData("struct S { int x; } static S[] M(int n) => new S[n];")]
    public void AnUnsupportedArrayCreationIsOpaque(string members) =>
        Assert.Equal("ArrayCreation", Assert.Single(Opaques(Method(members))).Reason);

    /// <summary>A <c>new.&lt;Sort&gt;</c> input: allocation <c>k</c> of <paramref name="sort"/> is element <c>k + 1</c>.</summary>
    /// <summary>
    /// The map each branch's condition reads, in block order (with the key when it is a parameter), or its name when a
    /// condition is not a map read.
    /// </summary>
    private static ImmutableArray<string> NullTests(IrProcedure procedure)
    {
        ImmutableArray<IrMapRead> reads = [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>()];
        // A fragment's threw flag (ticket M4-004) is not a null test.
        HashSet<IrVar> threw = [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>().Select(static o => o.Threw).OfType<IrVar>()];
        return
        [
            .. procedure.Blocks.Select(static b => b.Terminator).OfType<IrBranch>().Where(b => !threw.Contains(b.Cond)).Select(branch =>
                reads.FirstOrDefault(r => r.Target == branch.Cond) is { } read
                    ? read.Key.SourceName is { Length: > 0 } key ? $"{read.Map.Name}[{key}]" : read.Map.Name
                    : branch.Cond.Name),
        ];
    }

    private static IrMapValue Fresh(string sort) => new(
        new IrMap(new IrBitVec(32), new IrSort(sort)),
        new IrSortValue(sort, 99),
        ImmutableDictionary<IrValue, IrValue>.Empty.Add(Bits(32, 0), new IrSortValue(sort, 1)).Add(Bits(32, 1), new IrSortValue(sort, 2)));

    private static IrMapValue Input(IrVar parameter) => parameter.Name switch
    {
        ['n', 'e', 'w', '.', ..] => Fresh("int[]"),
        ['l', 'e', 'n', 'g', 't', 'h', '.', ..] => Lengths("int[]", 7),
        ['n', 'u', 'l', 'l', '.', ..] => Nulls("int[]", 0, isNull: false),
        _ => Elements("int[]", new IrBitVec(32)),
    };

    /// <summary>Ticket P1-006 acceptance criterion 2: the bounds check and <c>a.Length</c> read the length map at the array.</summary>
    [Fact]
    public void TheBoundsCheckAndLengthReadTheLengthMapAtTheArray()
    {
        IrProcedure procedure = Method("static int M(int[] a) => a[0] + a.Length;");

        IrVar length = Assert.Single(procedure.Parameters, static p => p.Var.Name is "length.int__").Var;
        ImmutableArray<IrMapRead> reads = [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>().Where(r => r.Map == length)];
        Assert.Equal(2, reads.Length);
        Assert.All(reads, static r => Assert.Equal("a", r.Key.Name));
    }

    /// <summary>Ticket M3-007 acceptance criterion 1: the maps a body writes are by-ref, the rest are inputs, all ordered by name.</summary>
    [Fact]
    public void HeapMapsAreRefAndNullAndLengthAreIn()
    {
        IrProcedure procedure = Method("int f; int M(int[] a, string s) { a[0] = f; return s == null ? a.Length : 0; }");

        Assert.Equal(
            [
                ("a", IrParameterKind.In),
                ("s", IrParameterKind.In),
                ("array.int__", IrParameterKind.Ref),
                ("field.C.f", IrParameterKind.Ref),
                ("length.int__", IrParameterKind.In),
                ("null.System.String", IrParameterKind.In),
                ("null.int__", IrParameterKind.In),
                ("this", IrParameterKind.In),
            ],
            procedure.Parameters.Select(static p => (p.Var.Name, p.Kind)));
    }

    /// <summary>
    /// Ticket M3-007 acceptance criterion 3: the early <c>return</c> is lowered before the field map exists, and still
    /// names the map's input, which is its final version on that path.
    /// </summary>
    [Fact]
    public void ExitsLoweredBeforeAFieldIsTouchedStillNameItsFinalVersion()
    {
        IrProcedure procedure = Method("static int f; static int M(int n) { if (n > 0) { return 0; } f = n; return 1; }");

        IrVar map = Assert.Single(procedure.Parameters, static p => p.Var.Name is "field.C.f").Var;
        ImmutableArray<IrOut> outs = [.. procedure.Blocks.Select(static b => b.Terminator).OfType<IrReturn>().Select(static r => Assert.Single(r.Outs))];
        Assert.All(outs, o => Assert.Equal(map, o.Param));
        Assert.Contains(outs, o => o.Final == map);
        Assert.Contains(outs, o => o.Final != map);
    }

    /// <summary>
    /// Ticket M3-007 acceptance criterion 7: a C# parameter declared <c>@this</c> (Roslyn names it <c>this</c>) does not
    /// collide with the receiver. <see cref="Lowered.Method"/> asserts that the result validates.
    /// </summary>
    [Fact]
    public void AParameterNamedThisIsNotTheReceiver()
    {
        IrProcedure procedure = Method("int f; int M(int @this) => f + @this;");

        Assert.Equal(["$this", "field.C.f", "this"], procedure.Parameters.Select(static p => p.Var.Name), StringComparer.Ordinal);
        Assert.False(IrParameterNames.IsSynthesised(procedure.Parameters[0].Var.Name));
        Assert.Equal("this", procedure.Parameters[0].Var.SourceName);
        Assert.Equal((new IrBitVec(32), new IrSort("C")), (procedure.Parameters[0].Var.Type, procedure.Parameters[^1].Var.Type));
    }

    /// <summary>Ticket M3-007 acceptance criterion 4: the final heap is one of the run's outs, so a write is observable.</summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(-3, -3)]
    public void AFieldWriteIsInTheRunsOuts(int n, int expected)
    {
        IrProcedure procedure = Method("static int f; static int M(int n) { if (n > 0) { return 0; } f = n; return 1; }");

        IrRun run = IrInterpreter.Run(
            procedure,
            new IrInputs([Bits(32, n), Fields("C", new IrBitVec(32))]),
            Equiv.TestSupport.IrGenOracle.Instance,
            Equiv.TestSupport.IrGen.StepBudget);

        IrMapValue final = Assert.IsType<IrMapValue>(Assert.Single(run.Outs));
        Assert.Equal(Bits(32, expected), final.Read(new IrSortValue("C", 0)));
    }

    /// <summary>
    /// Ticket P1-005 acceptance criterion 5: a call reads and writes every field map the body touches, so the callee sees
    /// the field written before it and the read after it sees the callee's write. Before the fix this returned 1.
    /// </summary>
    [Fact]
    public void Call_HavocsFieldsWrittenByCallee()
    {
        IrProcedure procedure = Method("int g; void Bump(int k) { g = unchecked(g + k); } static int M(C o) { o.g = 1; o.Bump(2); return o.g; }");
        IrSortValue o = Reference(1, "C");
        IrInputs inputs = new([.. procedure.Parameters.Select(p => p.Var.Name switch
        {
            "o" => o,
            "field.C.g" => Fields("C", new IrBitVec(32)),
            _ => (IrValue)Nulls("C", 1, isNull: false),
        })]);

        IrRun run = IrInterpreter.Run(procedure, inputs, new BumpOracle(), Equiv.TestSupport.IrGen.StepBudget);

        Assert.Equal(new IrReturned(Bits(32, 3)), run.Outcome);
        Assert.Equal(Bits(32, 3), Assert.IsType<IrMapValue>(Assert.Single(run.Outs)).Read(o));
    }

    /// <summary>A call first lowered before the body touches a field still pairs that field, so the later read sees its write.</summary>
    [Fact]
    public void ACallBeforeTheFirstTouchOfAFieldStillWritesIt()
    {
        IrProcedure procedure = Method("int g; void Bump(int k) { g = unchecked(g + k); } static int M(C o) { o.Bump(2); return o.g; }");
        IrSortValue o = Reference(1, "C");
        IrInputs inputs = new([.. procedure.Parameters.Select(p => p.Var.Name switch
        {
            "o" => o,
            "field.C.g" => Fields("C", new IrBitVec(32)),
            _ => (IrValue)Nulls("C", 1, isNull: false),
        })]);

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("field.C.g", Assert.Single(call.Heap).Map);
        Assert.Equal(new IrReturned(Bits(32, 2)), IrInterpreter.Run(procedure, inputs, new BumpOracle(), Equiv.TestSupport.IrGen.StepBudget).Outcome);
    }

    /// <summary>A body with no field or array access pairs nothing at its calls, and nullness and length maps are never paired.</summary>
    [Fact]
    public void OnlyFieldAndArrayMapsArePaired()
    {
        Assert.All(Calls(Method("static int M(string s) => s == null ? 0 : Math.Abs(s.Length);")), static c => Assert.Empty(c.Heap));
        Assert.Equal(
            ["array.int__", "field.C.g"],
            Assert.Single(Calls(Method("int g; static int M(C o, int[] a) { int n = a[0] + a.Length + o.g; return Math.Abs(n); }"))).Heap.Select(static h => h.Map),
            StringComparer.Ordinal);

        // An array creation writes length.int__, which makes it by-ref (P2-001), but a call cannot change an array's length.
        IrProcedure allocating = Method("static int M(int n) { int[] a = new int[n]; return Math.Abs(a[0]); }");
        Assert.Contains(allocating.Parameters, static p => p is { Var.Name: "length.int__", Kind: IrParameterKind.Ref });
        Assert.Equal(["array.int__"], Assert.Single(Calls(allocating)).Heap.Select(static h => h.Map), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, true)]
    [InlineData(-1, true)]
    public void AnIndexOutsideTheArrayThrowsIndexOutOfRange(int index, bool thrown)
    {
        IrProcedure procedure = Method("static int M(int[] a, int i) => a[i];");

        IrOutcome outcome = Run(procedure, Reference(0, "int[]"), Bits(32, index), Elements("int[]", new IrBitVec(32)), Lengths("int[]", 4), Nulls("int[]", 0, isNull: false));

        Assert.Equal(thrown, outcome is IrThrew { ExceptionType: "System.IndexOutOfRangeException" });
    }

    /// <summary>
    /// Ticket P2-017 acceptance criterion 2: the CLR null-checks a field's receiver, an element's array and a call's
    /// receiver at the <c>stfld</c>, <c>stelem</c>, <c>ldelem</c> or <c>callvirt</c>, after the operands it evaluates first, so
    /// with a null target an overflowing operand throws before the dereference does.
    /// </summary>
    [Theory]
    [InlineData("int f; static void M(C o, int n) { o.f = checked(n + 1); }")]
    [InlineData("static void M(int[] o, int n) { o[0] = checked(n + 1); }")]
    [InlineData("static int M(int[] o, int n) => o[checked(n + 1)];")]
    [InlineData("void G(int i) { } static void M(C o, int n) { o.G(checked(n + 1)); }")]
    [InlineData("public virtual int P { get; set; } static void M(C o, int n) { o.P = checked(n + 1); }")]
    [InlineData("int this[int i] => i; static int M(C o, int n) => o[checked(n + 1)];")]
    public void ANullTargetIsCheckedAfterItsOperands(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Equal(new IrThrew("System.OverflowException"), Run(procedure, [.. procedure.Parameters.Select(p => NullTarget(p.Var, int.MaxValue))]));
        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(procedure, [.. procedure.Parameters.Select(p => NullTarget(p.Var, 0))]));
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>
    /// A compound assignment reads before it evaluates the value, so the getter's <c>callvirt</c> throws first.
    /// </summary>
    [Fact]
    public void ACompoundAssignmentToANullReceiversPropertyThrowsBeforeTheValue()
    {
        IrProcedure procedure = Method("public virtual int P { get; set; } static void M(C o, int n) { o.P += checked(n + 1); }");

        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(procedure, [.. procedure.Parameters.Select(p => NullTarget(p.Var, int.MaxValue))]));
    }

    /// <summary>An argument for <see cref="ANullTargetIsCheckedAfterItsOperands"/>: the reference <c>o</c> is null, <c>n</c> is <paramref name="n"/>.</summary>
    private static IrValue NullTarget(IrVar parameter, int n) => parameter.Type switch
    {
        IrSort sort => Reference(0, sort.Name),
        IrMap { Key: IrSort sort } when parameter.Name.StartsWith("null.", StringComparison.Ordinal) => Nulls(sort.Name, 0, isNull: true),
        IrMap { Key: IrSort sort } when parameter.Name.StartsWith("length.", StringComparison.Ordinal) => Lengths(sort.Name, 1),
        IrMap { Key: IrSort sort, Value: IrMap elements } => Elements(sort.Name, elements.Value),
        IrMap { Key: IrSort sort, Value: var value } => Fields(sort.Name, value),
        _ => Bits(32, n),
    };

    /// <summary>Ticket M2-004 acceptance criterion 4: a throw inside a try goes to the matching catch.</summary>
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(1, 1)]
    public void AThrowInsideATryGoesToTheCatchThatCatchesItsType(int a, int expected) =>
        Assert.Equal(
            new IrReturned(Bits(32, expected)),
            Run(Method("""
                static int M(int a)
                {
                    try
                    {
                        if (a < 0) throw new ArgumentOutOfRangeException();
                        return a;
                    }
                    catch (ArgumentException)
                    {
                        return 10;
                    }
                }
                """), Bits(32, a)));

    /// <summary>The first catch whose type the thrown type converts to wins; a type no catch takes leaves the procedure.</summary>
    [Theory]
    [InlineData(2, 0, 10)]
    [InlineData(int.MaxValue, 2, 20)]
    [InlineData(6, 3, 1)]
    public void TheFirstCatchThatTakesTheThrownTypeWins(int a, int b, int expected)
    {
        IrProcedure procedure = Method("""
            static int M(int a, int b)
            {
                try
                {
                    int c = checked(a * b);
                    int d = a / b;
                    return 1;
                }
                catch (DivideByZeroException)
                {
                    return 10;
                }
                catch (ArithmeticException)
                {
                    return 20;
                }
            }
            """);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>A finally is duplicated onto the normal path, the return path and the throw path.</summary>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 2)]
    [InlineData(-1, 12)]
    public void AFinallyRunsOnEveryExitPath(int a, int expected)
    {
        IrProcedure procedure = Method("""
            static int M(int a)
            {
                int s = 0;
                try
                {
                    if (a < 0) throw new ArgumentException();
                    if (a > 0) { s = 1; return s + 1; }
                    s = 2;
                }
                catch (ArgumentException)
                {
                    return s + 12;
                }
                finally
                {
                    s = s + 1;
                }

                return s + 1;
            }
            """);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a)));
    }

    [Fact]
    public void AFinallyWithNoCatchStillRunsBeforeTheThrowLeaves()
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return a / b; } finally { Log(); } } static void Log() { }");

        Assert.Equal(new IrThrew("System.DivideByZeroException"), Run(procedure, Bits(32, 1), Bits(32, 0)));
        Assert.Equal(3, Calls(procedure).Length); // one copy of the finally per exit path: return, divide by zero, MinValue / -1
    }

    /// <summary>Exits that end the same way share one copy of the finally, instead of one per raising instruction.</summary>
    [Fact]
    public void ExitsThatLeaveTheSameWayShareOneFinallyCopy()
    {
        IrProcedure procedure = Method("static int M(int a, int b, int c) { try { return a / b + a / c; } finally { Log(); } } static void Log() { }");

        // Both divide-by-zero edges reach the same shared throw block, and so do both MinValue / -1 edges.
        Assert.Equal(3, Calls(procedure).Length);
    }

    /// <summary>An opaque call's exception type is unknown, so one candidate catch takes it and several are opaque.</summary>
    [Fact]
    public void ACallThatThrowsInsideATryGoesToTheOnlyCatch()
    {
        IrProcedure procedure = Method("static void F() { } static int M() { try { F(); return 1; } catch (ArgumentException) { return 2; } }");

        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow);
    }

    [Fact]
    public void ACallThatThrowsWhereSeveralCatchesCouldApplyIsOpaque()
    {
        IrProcedure procedure = Method("""
            static void F() { }
            static int M()
            {
                try { F(); return 1; }
                catch (ArgumentException) { return 2; }
                catch (InvalidOperationException) { return 3; }
            }
            """);

        Assert.Equal("call-throw-in-try", Assert.Single(Opaques(procedure)).Reason);
    }

    /// <summary>
    /// An exception raised inside a `finally` unwinds to a `catch` outside it, which only the main pass
    /// lowered: the copy's own block map does not name it.
    /// </summary>
    [Theory]
    [InlineData(2, 6)]
    [InlineData(0, 11)]
    public void AThrowInsideAFinallyGoesToACatchOutsideIt(int b, int expected)
    {
        IrProcedure procedure = Method("""
            static int M(int a, int b)
            {
                int s = 0;
                try
                {
                    try { s = 1; }
                    finally { s += 10 / b; }
                }
                catch (DivideByZeroException) { s += 10; }

                return s;
            }
            """);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, 0), Bits(32, b)));
    }

    /// <summary>The same, for a call's `threw` edge, whose exception type is not known.</summary>
    [Fact]
    public void ACallThatThrowsInsideAFinallyGoesToACatchOutsideIt()
    {
        IrProcedure procedure = Method("static void F() { } static int M() { try { try { return 1; } finally { F(); } } catch (ArgumentException) { return 2; } }");

        Assert.Empty(Opaques(procedure));
        Assert.DoesNotContain(procedure.Blocks, static b => b.Terminator is IrThrow);
    }

    /// <summary>A `catch` nested inside the `finally` is in the copy's own map, not the main pass's.</summary>
    [Fact]
    public void AThrowInsideAFinallyGoesToACatchInsideThatFinally()
    {
        IrProcedure procedure = Method("""
            static int M(int a, int b)
            {
                int s = 0;
                try { s = 1; }
                finally
                {
                    try { s += 10 / b; }
                    catch (DivideByZeroException) { s += 100; }
                }

                return s;
            }
            """);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 101)), Run(procedure, Bits(32, 0), Bits(32, 0)));
        Assert.Equal(new IrReturned(Bits(32, 6)), Run(procedure, Bits(32, 0), Bits(32, 2)));
    }

    /// <summary>A folded chain is still a set of ordinary edges: one that leaves a `try` runs its `finally`.</summary>
    [Theory]
    [InlineData(1, 11)]
    [InlineData(3, 13)]
    [InlineData(4, 10)]
    public void AFoldedSwitchRunsTheFinallyOnEveryEdge(int x, int expected)
    {
        IrProcedure procedure = Method("""
            static int M(int x)
            {
                int r = 0;
                try
                {
                    if (x == 1) { r = 1; }
                    else if (x == 2) { r = 2; }
                    else if (x == 3) { r = 3; }
                }
                finally { r += 10; }

                return r;
            }
            """);

        Assert.Single(procedure.Blocks.Select(static b => b.Terminator).OfType<IrSwitch>());
        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, x)));
    }

    /// <summary>
    /// A `finally` that never completes leaves the block after its `try` unreachable, so it is never lowered,
    /// though the `try`'s exits still name it (ticket P2-010). Each exit runs the `finally` and throws; the
    /// second shape has two exits to the unlowered block, which share one copy of the `finally`.
    /// </summary>
    [Theory]
    [InlineData("static int M(int k) { try { k = k + 1; } finally { throw new InvalidOperationException(); } }", 0)]
    [InlineData("static int M(int k) { try { if (k == 1) goto done; k = 2; } finally { throw new InvalidOperationException(); } done: return k; }", 0)]
    [InlineData("static int M(int k) { try { if (k == 1) goto done; k = 2; } finally { throw new InvalidOperationException(); } done: return k; }", 1)]
    public void AnExitThroughAFinallyThatNeverCompletesLowers(string members, int k)
    {
        IrProcedure procedure = Method(members);

        Assert.Empty(Opaques(procedure));
        Assert.Single(Calls(procedure)); // one copy of the finally
        Assert.Equal(new IrThrew("System.InvalidOperationException"), Run(procedure, Bits(32, k)));
    }

    /// <summary>
    /// A conditional `throw;` in a `catch` is one CFG block: its condition branches past the rethrow, and its
    /// fall-through is the rethrow itself, which names no block (found by the `gitextensions-8522` census,
    /// ticket P2-010). The rethrow is opaque as usual; the path that skips it still runs.
    /// </summary>
    [Theory]
    [InlineData(6, 2, 3)]
    [InlineData(1, 0, -1)]
    public void AConditionalRethrowInsideACatchLowers(int a, int b, int expected)
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) { if (a > 5) throw; } return -1; }");

        Assert.Equal("rethrow", Assert.Single(Opaques(procedure)).Reason);
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>
    /// `if (c) return; throw;` in a `catch` is one CFG block too, but the rethrow is its conditional successor, not its
    /// fall-through (found by the M4-007 run on `gitextensions-8522`, ticket P2-034). It lowered to a bare
    /// NullReferenceException; now the rethrow is opaque and the path that returns still runs.
    /// </summary>
    [Theory]
    [InlineData(6, 2, 3)]
    [InlineData(6, 0, 7)]
    public void AReturnBeforeARethrowInsideACatchLowers(int a, int b, int expected)
    {
        IrProcedure procedure = Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) { if (a > 5) return 7; throw; } }");

        Assert.Equal("rethrow", Assert.Single(Opaques(procedure)).Reason);
        Assert.Equal(new IrReturned(Bits(32, expected)), Run(procedure, Bits(32, a), Bits(32, b)));
    }

    /// <summary>
    /// The shape of Git Extensions' <c>ConfigureJoinableTaskFactoryAttribute.AfterTest</c> (ticket P2-034): a filtered
    /// <c>catch</c> whose body is an <c>if</c> and then <c>throw;</c>, so the <c>if</c>'s false edge is the rethrow.
    /// </summary>
    [Fact]
    public void AnIfThenRethrowInsideAFilteredCatchLowers() =>
        Assert.Contains(
            Opaques(Method("""
                static int s;
                static void M(System.Threading.CancellationTokenSource cts, string v)
                {
                    try
                    {
                        try { System.Threading.Thread.Sleep(1); }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested)
                        {
                            if (int.TryParse(v, out var sleep) && sleep > 0) { System.Threading.Thread.Sleep(sleep); }
                            throw;
                        }
                    }
                    finally { s = 0; }
                }
                """)),
            static o => string.Equals(o.Reason, "rethrow", StringComparison.Ordinal));

    [Fact]
    public void RethrowIsOpaqueInsideACatch() =>
        Assert.Equal(
            "rethrow",
            Assert.Single(Opaques(Method("static int M(int a, int b) { try { return a / b; } catch (DivideByZeroException) { throw; } }"))).Reason);

    /// <summary>Strings stay uninterpreted, so `a + b` is the call the compiler makes (needed by the `removed-null-check` sample).</summary>
    [Fact]
    public void StringConcatenationIsACallToStringConcat()
    {
        IrProcedure procedure = Method("static string M(string a, string b) => a + b;");

        Assert.Equal("System.String::Concat(string,string)", Assert.Single(Calls(procedure)).Callee.Value);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket M2-004 acceptance criterion 8: the shape of the `removed-null-check` sample lowers opaque-free.</summary>
    [Fact]
    public void TheRemovedNullCheckSampleShapeLowersWithoutOpaqueNodes()
    {
        IrProcedure guarded = Method("static string M(string name) { if (name == null) throw new ArgumentNullException(\"name\"); return \"Hello, \" + name.ToUpper(); }");
        IrProcedure unguarded = Method("static string M(string name) { return \"Hello, \" + name.ToUpper(); }");

        Assert.Empty(Opaques(guarded));
        Assert.Empty(Opaques(unguarded));
        Assert.Contains(guarded.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.ArgumentNullException" });
        Assert.Contains(unguarded.Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
    }

    [Fact]
    public void ReadWithoutDefinitionIsUndefined()
    {
        IrProcedure procedure = ErroneousBody("static int M() { int x; return x; }");

        IrOpaque opaque = Assert.IsType<IrOpaque>(procedure.Blocks[0].Instructions[0]);
        Assert.Equal("undefined", opaque.Reason);
    }

    /// <summary>
    /// Ticket P2-009: the census's <c>undefined</c> was the read of an <c>out</c> argument of a <c>ref-argument</c> opaque.
    /// Since ticket M4-003 only a call with a <c>ref</c> to a field or an array element, or with one variable written twice,
    /// is that opaque.
    /// </summary>
    [Theory]
    [InlineData("static int f; static int M() { P(ref f, out int n); return n; } static void P(ref int a, out int b) => b = a;", 2)]
    [InlineData("static int M() { int x; P(out x, out x); return x; } static void P(out int a, out int b) => a = b = 0;", 2)]
    [InlineData("static int M(int[] a) { System.Threading.Interlocked.Exchange(ref a[0], 1); return a.Length; }", 1)]
    [InlineData("static C M(int[] a, int b) { C c = new C(ref a[0], out b); return b > 0 ? c : null; } C(ref int x, out int y) => y = x;", 2)]
    public void AVariableWrittenByARefArgumentOpaqueIsDefined(string members, int opaques) =>
        AssertDefined(Method(members), "ref-argument", opaques);

    [Theory]
    [InlineData("static int M((int, int) t) { var (a, b) = t; return a + b; }", "DeconstructionAssignment", 3)]
    [InlineData("static int M((int, (int, int)) t, int[] xs) { int a; (a, (xs[0], _)) = t; return a; }", "DeconstructionAssignment", 2)]
    [InlineData("static int M(object o) => o is int x ? x : 0;", "switch-pattern", 2)]
    [InlineData("static int M(object o) => o is int _ ? 1 : 0;", "switch-pattern", 1)]
    [InlineData("static int M(string s) { int? d = (int?)P(s, out int n); return n; } static int P(string s, out int n) => n = 0;", "Conversion", 2)]
    public void AVariableWrittenByAnotherOpaqueIsDefined(string members, string reason, int opaques) =>
        AssertDefined(Method(members), reason, opaques);

    private static void AssertDefined(IrProcedure procedure, string reason, int opaques)
    {
        Assert.DoesNotContain(Opaques(procedure), static o => o.Reason is "undefined");
        Assert.Equal(opaques, Opaques(procedure).Count(o => string.Equals(o.Reason, reason, StringComparison.Ordinal)));
    }

    /// <summary>Ticket M4-003: <c>ref</c> and <c>out</c> arguments to locals and parameters lower with no opaque.</summary>
    [Theory]
    [InlineData("static int M(string s, int f) => int.TryParse(s, out var n) ? n : f;")]
    [InlineData("static int M(string s) { int.TryParse(s, out int n); return n; }")]
    [InlineData("static bool M(string s) => int.TryParse(s, out _);")]
    [InlineData("static int M(ref int a) { System.Threading.Interlocked.Exchange(ref a, 1); return a; }")]
    [InlineData("static string M(System.Collections.Generic.Dictionary<int, string> d) { d.TryGetValue(1, out string v); return v.Trim(); }")]
    [InlineData("static C M() { int b = 0; C c = new C(ref b); return b > 0 ? c : null; } C(ref int x) { }")]
    public void TryParseLowersWithoutOpaque(string members) => Assert.Empty(Opaques(Method(members)));

    /// <summary>Ticket M4-003: an <c>out</c> argument passes nothing and is the call's output after it.</summary>
    [Fact]
    public void OutArgumentIsACallOutput()
    {
        IrProcedure procedure = Method("static int M(string s) { int.TryParse(s, out int n); return n; }");

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal(["s"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
        IrVar output = Assert.Single(call.RefOuts);
        Assert.Equal(new IrBitVec(32), output.Type);
        Assert.Equal(Bits(32, 7), Assert.IsType<IrReturned>(RunWith(procedure, new RefOutOracle(Bits(32, 7)), Reference(1))).Value);
    }

    /// <summary>
    /// Ticket M4-003: a <c>ref</c> argument passes its value at the call, after the arguments that follow it are evaluated,
    /// and takes the call's output.
    /// </summary>
    [Fact]
    public void RefArgumentIsPassedAndReturned()
    {
        IrProcedure procedure = Method("static int M(int a) { P(ref a, a = 5); return a; } static void P(ref int x, int y) { }");
        RefOutOracle oracle = new(Bits(32, 9));

        Assert.Equal(Bits(32, 9), Assert.IsType<IrReturned>(RunWith(procedure, oracle, Bits(32, 1))).Value);
        Assert.Equal([Bits(32, 5), Bits(32, 5)], Assert.Single(oracle.Arguments));
    }

    /// <summary>
    /// Ticket M4-011: <c>lock</c> lowers through the CFG's <c>Monitor.Enter(o, ref lockTaken)</c> and
    /// <c>finally { if (lockTaken) Monitor.Exit(o); }</c>, with no opaque.
    /// </summary>
    [Fact]
    public void LockLowersThroughTryFinally()
    {
        IrProcedure procedure = Method("static int M(object o, int a) { lock (o) { a = a + 1; } return a; }");
        LockOracle oracle = new();

        Assert.Empty(Opaques(procedure));
        Assert.Equal(Bits(32, 4), Assert.IsType<IrReturned>(RunWith(procedure, oracle, Reference(1, "System.Object"), Bits(32, 3))).Value);
        Assert.Equal(
            ["System.Threading.Monitor::Enter(object,ref bool)", "System.Threading.Monitor::Exit(object)"],
            oracle.Calls.Select(static c => c.Callee),
            StringComparer.Ordinal);
    }

    /// <summary>Ticket M4-011 criterion 1: the compiler's <c>lockTaken</c> is false on every entry to the <c>lock</c>.</summary>
    [Fact]
    public void LockInALoopStartsUntakenEachIteration()
    {
        IrProcedure procedure = Method("static int M(object o, int n) { int k = 0; for (int i = 0; i < n; i++) { lock (o) { k++; } } return k; }");
        LockOracle oracle = new();

        Assert.Equal(Bits(32, 2), Assert.IsType<IrReturned>(RunWith(procedure, oracle, Reference(1, "System.Object"), Bits(32, 2))).Value);
        Assert.Equal(
            [new IrBoolValue(Value: false), new IrBoolValue(Value: false)],
            oracle.Calls.Where(static c => c.Callee.Contains("Enter", StringComparison.Ordinal)).Select(static c => c.Arguments[1]));
    }

    [Theory]
    [InlineData("static int f; static void M() { System.Threading.Interlocked.Increment(ref f); }")]
    [InlineData("int f; void M() { System.Threading.Interlocked.Increment(ref f); }")]
    [InlineData("static int M(int[] a) => System.Threading.Interlocked.Increment(ref a[0]);")]
    public void RefToAFieldStaysOpaque(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Equal("ref-argument", Assert.Single(Opaques(procedure)).Reason);
        Assert.Empty(Calls(procedure));
    }

    [Fact]
    public void FallingOffANonVoidMethodIsMissingReturn()
    {
        IrProcedure procedure = ErroneousBody("static int M() { goto missing; }");

        Assert.Contains(Opaques(procedure), static o => o.Reason is "missing-return");
        Assert.Contains(Opaques(procedure), static o => o.Reason is "Invalid" && o.Target is null);
    }

    [Theory]
    [InlineData(5, 7)]
    [InlineData(-3, -3)]
    public void IfElseSelectsTheTakenBranch(int a, int expected) =>
        Assert.Equal(
            new IrReturned(Bits(32, expected)),
            Run(Method("static int M(int a) { int r = a; if (a > 0) { r = a + 2; } return r; }"), Bits(32, a)));

    [Fact]
    public void UnsignedAndCharConstantsKeepTheirBits()
    {
        Assert.Equal(new IrReturned(Bits(32, 6)), Run(Method("static uint M(uint a) => a + 5u;"), Bits(32, 1)));
        Assert.Equal(new IrReturned(Bits(16, 97)), Run(Method("static char M() => 'a';")));
        Assert.Equal(new IrReturned(Bits(32, 5)), Run(Method("const int K = 5; static int M() => K;")));
    }

    [Fact]
    public void CharIsSixteenBitsWide() =>
        Assert.Equal(new IrReturned(Bits(32, 4464)), Run(Method("static int M(int a) => (char)a;"), Bits(32, 70000)));

    [Theory]
    [InlineData(0x7FFF_FFFFL, false)]
    [InlineData(0x8000_0000L, true)]
    public void CheckedUnsignedToSignedThrowsWhenNegative(long u, bool throws) =>
        Assert.Equal(
            throws ? new IrThrew("System.OverflowException") : new IrReturned(Bits(32, u)),
            Run(Method("static int M(uint u) => checked((int)u);"), Bits(32, u)));

    [Fact]
    public void CheckedImplicitWideningEmitsNoOverflowTest()
    {
        IrProcedure procedure = Method("static long M(int a, long b) => checked(a + b);");

        Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOverflows>());
    }

    [Fact]
    public void CheckedBitwiseOperationCannotOverflow() =>
        Assert.Empty(Method("static int M(int a, int b) => checked(a & b);").Blocks.SelectMany(static b => b.Instructions).OfType<IrOverflows>());

    [Theory]
    [InlineData(int.MinValue, "System.OverflowException")]
    [InlineData(5, null)]
    public void CheckedNegationOverflowsOnlyAtMinValue(int a, string? thrown) =>
        Assert.Equal(
            thrown is null ? new IrReturned(Bits(32, -a)) : new IrThrew(thrown),
            Run(Method("static int M(int a) => checked(-a);"), Bits(32, a)));

    [Fact]
    public void BitwiseNotAndUnaryPlusLower()
    {
        IrProcedure procedure = Method("static int M(int a) => +(~a);");

        Assert.Equal(new IrReturned(Bits(32, ~41)), Run(procedure, Bits(32, 41)));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void ValueTypeReceiverIsTheFirstArgument()
    {
        IrCall call = Assert.Single(Calls(Method("static string M(int a, IFormatProvider p) => a.ToString(p);")));

        Assert.Equal("System.Int32::ToString(System.IFormatProvider)", call.Callee.Value);
        Assert.Equal(["a", "p"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void NamedArgumentsArePassedInParameterOrder()
    {
        IrCall call = Assert.Single(Calls(Method("static int F(int x, int y) => x; static int M(int a, int b) => F(y: b, x: a);")));

        Assert.Equal(["a", "b"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
    }

    [Fact]
    public void GotoLoopGetsPhisAtItsHeader() =>
        Assert.Equal(
            new IrReturned(Bits(32, 6)),
            Run(Method("static int M(int n) { int s = 0; top: if (n > 0) { s = s + n; n = n - 1; goto top; } return s; }"), Bits(32, 3)));

    [Fact]
    public void NestedGotoLoopsRemoveEveryTrivialPhi()
    {
        IrProcedure procedure = Method("""
            static int M(int n, int m)
            {
                int s = 0;
                outer:
                if (n > 0)
                {
                    int k = m;
                    inner:
                    if (k > 0) { s = s + 1; k = k - 1; goto inner; }
                    n = n - 1;
                    goto outer;
                }
                return s;
            }
            """);

        Assert.Equal(new IrReturned(Bits(32, 6)), Run(procedure, Bits(32, 2), Bits(32, 3)));
        Assert.DoesNotContain(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPhi>(), static p => p.Target.SourceName is "m");
    }

    /// <summary>
    /// Ticket P2-006 acceptance criterion 1: <c>??=</c> on a field makes the CFG capture the field, and the assignment's
    /// target is that capture. The field's map is read once and written only when it held null.
    /// </summary>
    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 5)]
    public void ANullCoalescingAssignmentToAFieldWritesItOnlyWhenItWasNull(bool wasNull, int expected)
    {
        IrProcedure procedure = Method("static string? s; static string M(string t) => s ??= t;");
        IrMapValue field = new(new IrMap(new IrSort("C"), new IrSort("System.String")), Reference(5), []);
        IrInputs inputs = new([.. procedure.Parameters.Select(p => p.Var.Name switch
        {
            "t" => Reference(7),
            "field.C.s" => field,
            _ => (IrValue)Nulls("System.String", 5, wasNull),
        })]);

        IrRun run = IrInterpreter.Run(procedure, inputs, Equiv.TestSupport.IrGenOracle.Instance, Equiv.TestSupport.IrGen.StepBudget);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Reference(expected)), run.Outcome);
        Assert.Equal(Reference(expected), Assert.IsType<IrMapValue>(Assert.Single(run.Outs)).Read(new IrSortValue("C", 0)));
    }

    /// <summary>
    /// Ticket P2-006 acceptance criterion 1: a field assigned a value that branches, directly or by a compound assignment,
    /// is captured before the value, and the assignment's target is that capture.
    /// </summary>
    [Theory]
    [InlineData("static int f; static void M(bool b, int a) { f = b ? 1 : a; }", true, 1)]
    [InlineData("static int f; static void M(bool b, int a) { f = b ? 1 : a; }", false, 9)]
    [InlineData("static int f; static void M(bool b, int a) { f += b ? 1 : a; }", false, 9)]
    [InlineData("static int f; static void M(bool b, int a) { f = 4; f += b ? 1 : a; }", true, 5)]
    public void AFieldAssignedABranchingValueIsWrittenThroughTheCapture(string members, bool b, int expected)
    {
        IrProcedure procedure = Method(members);

        IrRun run = IrInterpreter.Run(
            procedure,
            new IrInputs([new IrBoolValue(b), Bits(32, 9), Fields("C", new IrBitVec(32))]),
            Equiv.TestSupport.IrGenOracle.Instance,
            Equiv.TestSupport.IrGen.StepBudget);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(Bits(32, expected), Assert.IsType<IrMapValue>(Assert.Single(run.Outs)).Read(new IrSortValue("C", 0)));
    }

    /// <summary>Ticket P2-006: a captured field's receiver is null-checked where the field is written, after the value.</summary>
    [Fact]
    public void AFieldOfANullReceiverAssignedABranchingValueThrows()
    {
        IrProcedure procedure = Method("sealed class H { public int F; } static void M(H h, bool b) { h.F = b ? 1 : 2; }");
        IrInputs inputs = new([.. procedure.Parameters.Select(p => p.Var.Type switch
        {
            IrSort sort => Reference(0, sort.Name),
            IrBool => new IrBoolValue(Value: true),
            IrMap { Value: IrBool } => Nulls("C+H", 0, isNull: true),
            _ => (IrValue)Fields("C+H", new IrBitVec(32)),
        })]);

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrThrew("System.NullReferenceException"), IrInterpreter.Run(procedure, inputs, Equiv.TestSupport.IrGenOracle.Instance, Equiv.TestSupport.IrGen.StepBudget).Outcome);
    }

    /// <summary>
    /// Ticket P2-006: <c>??=</c> on a property captures it; the capture's read is the getter call and its write the setter
    /// call, in a later block that runs only when the getter returned null. Before the fix the read was an undefined capture.
    /// </summary>
    [Fact]
    public void ANullCoalescingAssignmentToAPropertyCallsTheGetterThenTheSetter()
    {
        IrProcedure procedure = Method("sealed class H { string? v; public string? Name { get => v; set => v = value; } } static string M(H h, string t) => h.Name ??= t;");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(["C.H::get_Name()", "C.H::set_Name(string)"], Calls(procedure).Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal(2, procedure.Blocks.Count(static b => b.Instructions.OfType<IrCall>().Any()));
    }

    [Fact]
    public void CapturedLocalIsAssignedThroughTheCapture() =>
        Assert.Equal(
            new IrReturned(Bits(32, 3)),
            Run(Method("static int M(bool b, int a) { int x = 0; x = b ? a : -a; return x; }"), new IrBoolValue(Value: true), Bits(32, 3)));

    [Fact]
    public void DivisionByZeroThrows() =>
        Assert.Equal(new IrThrew("System.DivideByZeroException"), Run(Method("static uint M(uint a, uint b) => a / b;"), Bits(32, 1), Zero32));

    /// <summary>Ticket M3-010 acceptance criterion 1: a property read is the getter call, receiver null check included.</summary>
    [Fact]
    public void PropertyReadIsACallToTheGetter()
    {
        IrProcedure procedure = Method("public virtual int P { get; set; } static int M(C c) => c.P;");

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("C::get_P()", call.Callee.Value);
        Assert.Equal(["c"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(procedure, Reference(0, "C"), Nulls("C", 0, isNull: true)));
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket M3-010 acceptance criterion 2: a write is the setter call with the value last and no target.</summary>
    [Fact]
    public void PropertyWriteIsACallToTheSetter()
    {
        IrProcedure procedure = Method("static int p; static int P { get => p; set => p = value; } static void M(int a) { P = a; }");

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("C::set_P(int)", call.Callee.Value);
        Assert.Null(call.Target);
        Assert.Equal(["a"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>Ticket M3-010 acceptance criterion 2: the receiver is evaluated once and passed to both accessors.</summary>
    [Theory]
    [InlineData("void M(C c, int a) { c.P += a; }", false)]
    [InlineData("void M(C c, int a) { c.P++; }", false)]
    [InlineData("void M(C c, int a) { checked { --c.P; } }", true)]
    [InlineData("void M(C c, int a) { c.P <<= a; }", false)]
    public void CompoundAssignmentToAPropertyGetsOperatesAndSets(string member, bool isChecked)
    {
        IrProcedure procedure = Method($"public virtual int P {{ get; set; }} {member}");

        ImmutableArray<IrCall> calls = Calls(procedure);
        Assert.Equal(["C::get_P()", "C::set_P(int)"], calls.Select(static c => c.Callee.Value), StringComparer.Ordinal);
        Assert.Equal(calls[0].Args, calls[1].Args[..1]);
        Assert.Equal(2, calls[1].Args.Length);
        Assert.Equal(isChecked, procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOverflows>().Any());
        Assert.Empty(Opaques(procedure));
    }

    /// <summary>The CFG captures the written property before a branching value; the capture calls no getter.</summary>
    [Theory]
    [InlineData("void M(C c, bool b, int a) { c.P = b ? a : 0; }", "C::set_P(int)")]
    [InlineData("void M(C c, bool b, int a) { c.P += b ? a : 0; }", "C::get_P(),C::set_P(int)")]
    public void APropertyCapturedAsAnAssignmentTargetIsWrittenByItsAccessors(string member, string callees)
    {
        IrProcedure procedure = Method($"public virtual int P {{ get; set; }} {member}");

        ImmutableArray<IrCall> calls = Calls(procedure);
        Assert.Equal(callees, string.Join(',', calls.Select(static c => c.Callee.Value)));
        Assert.All(calls, static c => Assert.Equal("c", c.Args[0].Name));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void IndexerReadPassesTheIndex()
    {
        IrProcedure procedure = Method("int this[int i] => i; static int M(C c, int i) => c[i];");

        IrCall call = Assert.Single(Calls(procedure));
        Assert.Equal("C::get_Item(int)", call.Callee.Value);
        Assert.Equal(["c", "i"], call.Args.Select(static a => a.Name), StringComparer.Ordinal);
    }

    /// <summary>
    /// Ticket M3-010 acceptance criterion 3: an access with no accessor for it stays opaque and calls no setter. Code
    /// that writes an init-only setter outside an initializer does not compile (and erroneous code is one whole-body
    /// opaque), so the init-only case that binds is the initializer itself, which is out of this ticket's scope.
    /// </summary>
    [Theory]
    [InlineData("class D { public virtual int P { get; init; } } static D M() => new D { P = 1 };")]
    [InlineData("static int f; static ref int P => ref f; static void M() { P = 1; }")]
    [InlineData("static int f; static ref int P => ref f; static void M() { P++; }")]
    [InlineData("static int f; static ref int P => ref f; static void M(int a) { P += a; }")]
    public void InitOnlySetterOutsideInitializerIsOpaque(string members)
    {
        IrProcedure procedure = Method(members);

        Assert.Contains(Opaques(procedure), static o => o.Reason is "PropertyReference");
        Assert.DoesNotContain(Calls(procedure), static c => c.Callee.Value.Contains("set_P", StringComparison.Ordinal));
    }

    /// <summary>Ticket M3-010 acceptance criterion 4: an upcast reads <c>cast.&lt;From&gt;.&lt;To&gt;</c> at the operand.</summary>
    [Theory]
    [InlineData("static object M(string s) => s;", "cast.System.String.System.Object", "System.Object")]
    [InlineData("static IComparable M(string s) => s;", "cast.System.String.System.IComparable", "System.IComparable")]
    public void UpcastIsAReadOfTheCastMap(string members, string name, string to)
    {
        IrProcedure procedure = Method(members);

        IrParameter cast = Assert.Single(procedure.Parameters, p => string.Equals(p.Var.Name, name, StringComparison.Ordinal));
        Assert.Equal(IrParameterKind.In, cast.Kind);
        Assert.Equal(new IrMap(new IrSort("System.String"), new IrSort(to)), cast.Var.Type);
        IrMapRead read = Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrMapRead>(), r => r.Map == cast.Var);
        Assert.Equal((cast.Var, "s"), (read.Map, read.Key.Name));
        Assert.Empty(Opaques(procedure));
    }

    [Fact]
    public void BoxingIsAReadOfTheCastMap()
    {
        IrProcedure procedure = Method("static object M(int a) => a;");

        IrParameter cast = Assert.Single(procedure.Parameters, static p => p.Var.Name is "cast.System.Int32.System.Object");
        Assert.Equal(new IrMap(new IrBitVec(32), new IrSort("System.Object")), cast.Var.Type);
        IrMapValue map = new(
            (IrMap)cast.Var.Type,
            new IrSortValue("System.Object", 1),
            ImmutableDictionary<IrValue, IrValue>.Empty.Add(Bits(32, 7), new IrSortValue("System.Object", 9)));
        Assert.Equal(new IrReturned(new IrSortValue("System.Object", 9)), Run(procedure, Bits(32, 7), map));
    }

    /// <summary>A cast is a trace-free function, and its result's nullness comes from the target sort's map, not the operand's shadow.</summary>
    [Fact]
    public void CastMapAddsNoTraceEvent()
    {
        IrProcedure procedure = Method("static bool M(string s) { if (s == null) return false; object o = s; return o == null; }");

        Assert.Empty(Calls(procedure));
        Assert.Empty(Opaques(procedure));
        Assert.Contains(procedure.Parameters, static p => p.Var.Name is "null.System.Object");
    }

    /// <summary>Ticket P2-030 acceptance criterion 2: Roslyn folds <c>sizeof(int)</c> to the constant 4, which lowers like any other constant.</summary>
    [Fact]
    public void SizeOfABuiltInTypeFoldsToItsConstant()
    {
        IrProcedure procedure = Method("static int M() => sizeof(int);");

        Assert.Empty(Opaques(procedure));
        Assert.Equal(new IrReturned(Bits(32, 4)), Run(procedure));
    }

    /// <summary>Ticket P2-030 acceptance criterion 1: a user-defined struct's <c>sizeof</c> is layout-dependent and stays opaque with reason <c>SizeOf</c>.</summary>
    [Fact]
    public void SizeOfAUserDefinedStructIsOpaque() =>
        Assert.Contains(
            Opaques(ErroneousBody("struct S { public int X; } static int M() => sizeof(S);")),
            static o => string.Equals(o.Reason, "SizeOf", StringComparison.Ordinal));

    /// <summary>Ticket P2-002 acceptance criterion 1: <c>typeof(T)</c> for a closed <c>T</c> reads a shared <c>typeof.&lt;T&gt;</c> input, adding no trace event.</summary>
    [Fact]
    public void TypeOfIsAReadOfASharedInput()
    {
        IrProcedure procedure = Method("static Type M() => typeof(string);");

        IrParameter parameter = Assert.Single(procedure.Parameters, static p => p.Var.Name is "typeof.System.String");
        Assert.Equal(IrParameterKind.In, parameter.Kind);
        Assert.Equal(new IrSort("System.Type"), parameter.Var.Type);
        IrReturn ret = Assert.IsType<IrReturn>(Assert.Single(procedure.Blocks, static b => b.Terminator is IrReturn).Terminator);
        Assert.Equal(parameter.Var, ret.Value);
        Assert.Empty(Calls(procedure));
        Assert.Empty(Opaques(procedure));
    }

    /// <summary><c>typeof(T)</c> is never null, so an equality test against <c>null</c> is always false.</summary>
    [Fact]
    public void TypeOfIsNeverNull()
    {
        IrProcedure procedure = Method("static bool M() => typeof(string) == null;");

        Assert.Equal(new IrReturned(new IrBoolValue(Value: false)), Run(procedure, new IrSortValue("System.Type", 1)));
    }

    /// <summary>Runs <paramref name="procedure"/> with <paramref name="arguments"/> and every heap input after them empty, so nothing is null.</summary>
    private static IrOutcome RunWith(IrProcedure procedure, Equiv.Core.ICallOracle oracle, params IrValue[] arguments) =>
        IrInterpreter.Run(
            procedure,
            new IrInputs([.. arguments, .. procedure.Parameters.Skip(arguments.Length).Select(static p => new IrMapValue((IrMap)p.Var.Type, new IrBoolValue(Value: false), []))]),
            oracle,
            Equiv.TestSupport.IrGen.StepBudget).Outcome;

    /// <summary>Answers every call with no value, the same value for each of its <c>ref</c> and <c>out</c> outputs, and records each call's arguments.</summary>
    private sealed class RefOutOracle(IrValue output) : Equiv.Core.ICallOracle
    {
        public List<ImmutableArray<IrValue>> Arguments { get; } = [];

        public IrCallResult Answer(Equiv.Core.CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Arguments.Add(arguments);
            return new IrCallResult(resultType is IrBool ? new IrBoolValue(Value: true) : null, Threw: false) { RefOuts = [.. refOuts.Select(_ => output)] };
        }
    }

    /// <summary>Answers <c>Monitor.Enter</c> by taking the lock, sets every other <c>ref</c> output false, and records each call.</summary>
    private sealed class LockOracle : Equiv.Core.ICallOracle
    {
        public List<(string Callee, ImmutableArray<IrValue> Arguments)> Calls { get; } = [];

        public IrCallResult Answer(Equiv.Core.CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Calls.Add((callee.Value, arguments));
            return new IrCallResult(Value: null, Threw: false) { RefOuts = [.. refOuts.Select(_ => new IrBoolValue(Value: true))] };
        }
    }

    /// <summary>Answers <c>C::Bump(int)</c> as the compiled method does: it adds its argument to the receiver's <c>g</c>.</summary>
    private sealed class BumpOracle : Equiv.Core.ICallOracle
    {
        public IrCallResult Answer(Equiv.Core.CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            Assert.Equal("C::Bump(int)", callee.Value);
            return new IrCallResult(Value: null, Threw: false) { Heap = [.. heap.Select(h => h.Map is "field.C.g" ? Bumped((IrMapValue)h.Value, arguments) : h.Value)] };
        }

        private static IrMapValue Bumped(IrMapValue g, ImmutableArray<IrValue> arguments)
        {
            long sum = ((IrBitVecValue)g.Read(arguments[0])).TwosComplement + ((IrBitVecValue)arguments[1]).TwosComplement;
            return g.Write(arguments[0], Bits(32, sum));
        }
    }
}
