using System.Collections.Immutable;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.Frontend.CSharp.Lowering.Il;
using Equiv.TestSupport;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.IL;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>Tickets P1-014 and P1-015: the IL lowering of a method read back from its compilation's IL (ADR 0039).</summary>
public sealed class IlLowererTests
{
    /// <summary>The keys P1-014 lowered, which its criterion 4 compares the callee identities of.</summary>
    private static readonly ImmutableHashSet<string> ControlFlowAndCalls =
    [
        "ILFunction", "BlockContainer", "Block", "Nop", "Branch", "Leave", "IfInstruction", "SwitchInstruction", "SwitchSection",
        "LdLoc", "StLoc", "LdcI4", "LdcI8", "LdStr", "LdNull", "BinaryNumericInstruction", "Comp", "Conv", "Call", "CallVirt", "NewObj",
    ];

    /// <summary>Acceptance criterion 3: every sample method with a body lowers to IR that validates.</summary>
    [Fact]
    public void EverySampleMethodLowersToValidIr()
    {
        List<string> invalid = [];
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                ImmutableArray<IrDiagnostic> diagnostics = IrValidator.Validate(IlLowerer.Lower(procedure.Symbol, compilation, Runtimes.Migration));
                invalid.AddRange(diagnostics.Select(d => $"{sample} {procedure.Identity.Value}: {d.Message}"));
            }
        }

        Assert.True(invalid.Count == 0, string.Join('\n', invalid));
    }

    /// <summary>
    /// Acceptance criterion 3: in every sample method, each instruction with an unmapped key that no unmapped instruction
    /// holds is an opaque whose reason is that key, and every opaque's reason is a key of the method's ILAst.
    /// </summary>
    [Fact]
    public void UnmappedInstructionsAreOpaqueWithTheirKey()
    {
        List<string> wrong = [];
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                ILFunction function = IlAstReader.Read(procedure.Symbol, compilation).Function!;
                HashSet<ILVariable> caught = IlKeys.CaughtException(function);
                ImmutableHashSet<string> keys = [.. function.Descendants.Select(i => IlKeys.Key(i, caught))];
                ImmutableHashSet<string> unmapped = [.. function.Descendants.Where(i => Outermost(i, caught)).Select(i => IlKeys.Key(i, caught))];
                ImmutableHashSet<string> reasons = [.. Opaques(IlLowerer.Lower(procedure.Symbol, compilation, Runtimes.Migration)).Select(static o => o.Reason)];
                wrong.AddRange(unmapped.Except(reasons).Select(k => $"{sample} {procedure.Identity.Value}: {k} is not an opaque"));
                wrong.AddRange(reasons.Except(keys).Remove("undefined").Select(r => $"{sample} {procedure.Identity.Value}: {r} is no key of its ILAst"));
            }
        }

        Assert.True(wrong.Count == 0, string.Join('\n', wrong));
    }

    /// <summary>
    /// P1-014's criterion 4: a sample method the IOperation lowering lowers with no opaque and whose ILAst has only that
    /// ticket's keys names the same callees in the same order, and the same parameters with the same sorts, both ways. The
    /// heap, casts and exceptions P1-015 lowers are compared by <c>IlLoweringParityTests</c>, as equivalence under Z3.
    /// </summary>
    [Fact]
    public void CallIdentitiesMatchTheOperationLowering()
    {
        List<string> different = [];
        int compared = 0;
        foreach ((string sample, Compilation compilation) in IlSamples.All)
        {
            foreach (EnumeratedProcedure procedure in ProcedureEnumerator.Enumerate(compilation))
            {
                IrProcedure operation = IrLowerer.Lower(procedure.Symbol, compilation, RenameMap.Empty, [], Runtimes.Migration);
                ILFunction function = IlAstReader.Read(procedure.Symbol, compilation).Function!;
                HashSet<ILVariable> caught = IlKeys.CaughtException(function);
                if (!Opaques(operation).IsEmpty || !function.Descendants.All(i => ControlFlowAndCalls.Contains(IlKeys.Key(i, caught))))
                {
                    continue;
                }

                compared++;
                IrProcedure il = IlLowerer.Lower(procedure.Symbol, compilation, Runtimes.Migration);
                string expected = Signature(operation);
                string actual = Signature(il);
                if (!string.Equals(expected, actual, StringComparison.Ordinal) && !UnreachableNoMatchArm.Contains($"{sample} {procedure.Identity.Value}"))
                {
                    different.Add($"{sample} {procedure.Identity.Value}:\n  operation {expected}\n  il        {actual}\n{IrText.Dump(il)}");
                }
            }
        }

        Assert.True(different.Count == 0, string.Join('\n', different));
        Assert.True(compared > 10, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"only {compared} sample methods compared"));
    }

    /// <summary>
    /// Sample methods whose two lowerings name different callees for a reason that is not a mapping difference: a
    /// <c>switch</c> expression that ends in a discard arm. Roslyn's control-flow graph keeps the no-match block, which
    /// constructs a <c>SwitchExpressionException</c>, behind a branch that is never taken; the compiler emits no IL for it.
    /// The two are Equivalent under Z3 (<c>IlLoweringParityTests</c>).
    /// </summary>
    private static readonly string[] UnreachableNoMatchArm = ["same-runtime-cleanup/modern Equiv.Samples.SameRuntimeCleanup.Report::Rank(int)"];

    /// <summary>The Design's pitfall: the instruction's sign decides the division, not its operands' C# types.</summary>
    [Fact]
    public void UnsignedDivisionFollowsTheInstructionSign()
    {
        IrProcedure procedure = Lower("static int M(int a, int b) => (int)((uint)a / (uint)b);");

        Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions), static i => i is IrBinary { Op: IrBinaryOp.UDiv });
        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, int.MaxValue)), Run(procedure, IrBitVecValue.FromSigned(32, -1), IrBitVecValue.FromSigned(32, 2)));
    }

    /// <summary>A loop is a container whose back edge is a branch, and a <c>break</c> a leave of it.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(0)]
    public void LoopsAndNestedContainersAgreeWithTheInterpreter(int n) => Agree(
        "static int M(int n) { int s = 0; for (int i = 0; i < n; i++) { for (int j = 0; j < i; j++) { if (j == 3) break; if (j == 1) continue; s += j; } if (i > 6) break; } return s; }",
        n);

    [Fact]
    public void ASwitchIsOneIrSwitch()
    {
        const string Members = "static int M(int a) { switch (a) { case 1: return 10; case 2: case 3: return 20; case 7: return 5; default: return -1; } }"
            + "\nstatic long L(long a) { switch (a) { case 1: return 2; case 5: return 7; case 9: return 1; default: return 0; } }";

        IrSwitch choice = Assert.Single(Lower(Members).Blocks.Select(static b => b.Terminator).OfType<IrSwitch>());

        Assert.Equal(4, choice.Cases.Length);
        foreach (int a in (int[])[1, 3, 7, 9, -1])
        {
            Agree(Members, a);
        }

        foreach (long a in (long[])[5, 9, 4000000000])
        {
            Agree(Members, "L", a);
        }
    }

    /// <summary>A constant is of the type it is used as: an enum's element, a Bool, a narrow bitvector, a string, <c>null</c>.</summary>
    [Theory]
    [InlineData("enum E { A, B = 3 } static E M() => E.B;")]
    [InlineData("static bool M() => true;")]
    [InlineData("static byte M() => 200;")]
    [InlineData("static long M() => 1099511627776;")]
    [InlineData("static string M() => \"x\";")]
    [InlineData("static string M() => null;")]
    public void ConstantsTakeTheTypeTheyAreUsedAs(string members)
    {
        Compilation compilation = Compile(members);
        IMethodSymbol method = Method(compilation, "M");

        IrConst il = Assert.Single(IlLowerer.Lower(method, compilation, Runtimes.Migration).Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>());
        IrConst operation = Assert.Single(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration).Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>());

        Assert.Equal(operation.Value, il.Value);
    }

    /// <summary>A string passed where <c>object</c> is wanted reads the <c>cast</c> map, as the IOperation lowering's conversion does.</summary>
    [Fact]
    public void AReferenceIsUpcastThroughTheCastMap() =>
        Assert.Contains(Lower("static object M(string s) => Id(s); static object Id(object o) => o;").Parameters, static p => string.Equals(p.Var.Name, "cast.System.String.System.Object", StringComparison.Ordinal));

    [Theory]
    [InlineData("(sbyte)v")]
    [InlineData("(byte)v")]
    [InlineData("(short)v")]
    [InlineData("(ushort)v")]
    [InlineData("(char)v")]
    [InlineData("(int)v")]
    [InlineData("(uint)v")]
    [InlineData("(long)(ulong)v")]
    [InlineData("checked((sbyte)v)")]
    [InlineData("checked((byte)v)")]
    [InlineData("checked((short)v)")]
    [InlineData("checked((ushort)v)")]
    [InlineData("checked((int)v)")]
    [InlineData("checked((uint)v)")]
    [InlineData("checked((long)(ulong)v)")]
    [InlineData("checked((long)(ulong)(uint)(int)v)")]
    [InlineData("checked((int)(uint)(int)v)")]
    public void ConversionsToEveryIntegralType(string conversion)
    {
        string members = $"static long M(long v) => {conversion};";
        foreach (long v in (long[])[0, 5, -1, 200, 70000, -40000, 3000000000, -3000000000, long.MinValue, long.MaxValue])
        {
            Agree(members, v);
        }
    }

    /// <summary>
    /// An auto-property's accessor is its backing field's map, read at an upcast receiver, at <c>this</c>, or at the static
    /// token, and a user-defined operator its <c>op:</c> function, each as the IOperation lowering lowers it.
    /// </summary>
    [Fact]
    public void OperatorsAndAutoPropertiesAreLoweredAsTheOperationLoweringLowersThem()
    {
        Compilation compilation = RoslynTestCompilations.Compile(
            "class B { public int P { get; set; } }\n"
            + "class C : B { public static int Q { get; set; } public static C operator +(C a, C b) => a; int M(C o) { P = 3; Q = P; o = o + o; return o.P + Q; } }\n");
        IMethodSymbol method = compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        IrProcedure il = IlLowerer.Lower(method, compilation, Runtimes.Migration);
        IrProcedure operation = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);

        Assert.Empty(Opaques(il));
        Assert.Empty(il.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());
        Assert.Equal(Signature(operation), Signature(il));
        Assert.Equal(
            operation.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function),
            il.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A <c>callvirt</c> throws <c>System.NullReferenceException</c> on a null receiver after its arguments; <c>this</c> and a
    /// new object are never null, and <c>null</c> always is.
    /// </summary>
    [Fact]
    public void ACallvirtChecksItsReceiver()
    {
        IrProcedure length = Lower("static int M(string s) => s.Length;");
        IrMapValue nulls = new(new IrMap(new IrSort("System.String"), new IrBool()), new IrBoolValue(Value: true), []);

        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(length, new IrSortValue("System.String", 1), nulls));
        Assert.DoesNotContain(Lower("public virtual int V() => 1; public int M() => V() + new object().GetHashCode();").Blocks, static b => b.Terminator is IrThrow { ExceptionType: "System.NullReferenceException" });
        Assert.Equal(new IrThrew("System.NullReferenceException"), Run(Lower("static int M() { string s = null; return s.Length + ((string)null).Length; }")));
    }

    /// <summary>
    /// A mapped instruction the IR has no types for is an opaque of its own key after its operands, each lowered: a
    /// comparison of two references, a conversion of a native integer, a pointer local, a write through a <c>ref</c>
    /// local, an enum used as an integer, a call writing one variable through two <c>ref</c> arguments or constrained to a
    /// type parameter or returning by reference, a type test of a value type, the default of a struct, and a type token
    /// read other than by <c>typeof</c>.
    /// </summary>
    [Theory]
    [InlineData("static bool M(object o, int a) => o == G(a); static object G(int a) => null;", "Comp", "C::G(int)")]
    [InlineData("static int M(nint n, int a) => (int)n + F(a); static int F(int a) => a;", "Conv", "C::F(int)")]
    [InlineData("static bool M(nint n, int a) => n < F(a); static int F(int a) => a;", "Comp", "C::F(int)")]
    [InlineData("static unsafe int* M(int* p) => p;", "LdLoc", null)]
    [InlineData("static unsafe int*[] M(int*[] a) => a;", "LdLoc", null)]
    [InlineData("static void M(int[] a) { ref int r = ref a[0]; r = F(1); } static int F(int a) => a;", "StObj", "C::F(int)")]
    [InlineData("enum E { A } static int M(E e) => (int)e + F(1); static int F(int a) => a;", "LdLoc", "C::F(int)")]
    [InlineData("static int M(int a) { F(ref a, ref a); return a; } static void F(ref int a, ref int b) { }", "Call", null)]
    [InlineData("static string M<T>(T t) => t.ToString();", "CallVirt", null)]
    [InlineData("static void M(int[] a) => R(a); static ref int R(int[] a) => ref a[0];", "Call", null)]
    [InlineData("static bool M(object o) => o is int;", "IsInst", null)]
    [InlineData("static DateTime M(int a) { F(a); return default; } static int F(int a) => a;", "DefaultValue", "C::F(int)")]
    [InlineData("static Type M<T>() => typeof(T);", "LdTypeToken", null)]
    public void RefusedInstructionsLowerTheirOperands(string members, string reason, string? callee)
    {
        IrProcedure procedure = Lower(members);

        Assert.Contains(Opaques(procedure), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
        if (callee is not null)
        {
            Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>(), c => string.Equals(c.Callee.Value, callee, StringComparison.Ordinal));
        }
    }

    /// <summary>A key refined by position, found in the ILAst of a construct that has it, and, for an unmapped key, the opaque it makes.</summary>
    [Theory]
    [InlineData("static int M(int a) { try { return 10 / a; } catch (Exception e) { return e.HResult; } }", "LdLoc[caught exception]")]
    [InlineData("static void M() { try { F(); } catch (Exception e) { R(ref e); } } static void F() { } static void R(ref Exception e) { }", "LdLoca[caught exception]")]
    [InlineData("static int M(int[] a) { ref int r = ref a[0]; r++; return r + r; }", "StLoc[ref local]")]
    [InlineData("static unsafe int M() { int* p = stackalloc int[1]; return *p; }", "StLoc[ref local]")]
    [InlineData("static int M(string s) => int.TryParse(s, out int n) ? n : 0;", "LdLoca[address operand]")]
    [InlineData("static int M(int[] a) { ref int r = ref a[0]; r++; return r + r; }", "LdElema[address escapes]")]
    [InlineData("static int M(int[] a) { a[0] += 1; return a[0]; }", "LdElema[address operand]")]
    [InlineData("struct S { public int X; } static S s; static int M() => s.X;", "LdsFlda[address operand]")]
    [InlineData("struct S { public int X; public P Y; } struct P { public int Z; } static S s; static int M() => s.Y.Z;", "LdFlda[address operand]")]
    [InlineData("struct S { public int X; } static int M(S[] a) => a[0].X;", "LdElema[address operand]")]
    [InlineData("int f; void M() { f = 1; }", "LdFlda[address operand]")]
    [InlineData("static void M() => throw new InvalidOperationException();", "Throw[new]")]
    [InlineData("static void M(Exception e) => throw e;", "Throw[not new]")]
    [InlineData("static int[,] M() => new int[2, 3];", "NewArr[rank > 1]")]
    [InlineData("static T M<T>() => default(T);", "DefaultValue[type parameter]")]
    [InlineData("static DateTime M() => default;", "DefaultValue")]
    [InlineData("static Func<int> M(int a) => () => a;", "NewObj[closure class]")]
    [InlineData("static int M(int a) { return L(); int L() => a + 1; }", "Call[local function]")]
    [InlineData("static int M() => new { X = 1 }.X;", "Call[compiler-generated method]")]
    [InlineData("static Func<int> M() => () => 1;", "LdFtn[lambda]")]
    [InlineData("static Func<int> M() => new Func<int>(N); static int N() => 1;", "LdFtn")]
    [InlineData("static int M(int a) => ~a;", "BitNot")]
    public void KeysAreRefinedByPosition(string members, string key)
    {
        Compilation compilation = Compile(members);
        IMethodSymbol method = Method(compilation, "M");
        ILFunction function = IlAstReader.Read(method, compilation).Function!;
        HashSet<ILVariable> caught = IlKeys.CaughtException(function);

        Assert.Contains(key, function.Descendants.Select(i => IlKeys.Key(i, caught)), StringComparer.Ordinal);
        if (!IlKeys.Lowered.Contains(key))
        {
            Assert.NotEmpty(Opaques(IlLowerer.Lower(method, compilation, Runtimes.Migration)));
        }
    }

    /// <summary>
    /// Every ILAst key, every one the samples and <see cref="KeysAreRefinedByPosition"/> hold, and every key this ticket
    /// lowers has a row in <c>docs/tickets/IL-COVERAGE.md</c>; an opcode that is always refined has rows for its refinements.
    /// </summary>
    [Fact]
    public void EveryKeyOfTheTableHasARow()
    {
        ImmutableHashSet<string> rows =
        [
            .. File.ReadLines(Path.Combine(IlSamples.RepoRoot, "docs", "tickets", "IL-COVERAGE.md"))
                .Where(static l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| ILAst key", StringComparison.Ordinal))
                .Select(static l => l.Split('|')[1].Trim()),
        ];
        string[] refined = ["LdLoca", "LdFlda", "LdsFlda", "LdElema", "AddressOf", "Throw"];
        ImmutableHashSet<string> sampled =
        [
            .. IlSamples.All.SelectMany(static s => ProcedureEnumerator.Enumerate(s.Compilation).Select(p => IlAstReader.Read(p.Symbol, s.Compilation).Function!))
                .SelectMany(static f => f.Descendants.Select(i => IlKeys.Key(i, IlKeys.CaughtException(f)))),
        ];

        ImmutableArray<string> missing = [.. Enum.GetNames<OpCode>().Except(refined, StringComparer.Ordinal).Concat(sampled).Concat(IlKeys.Lowered).Where(k => !rows.Contains(k))];
        Assert.True(missing.IsEmpty, string.Join(", ", missing));
        Assert.All(refined, r => Assert.Contains(rows, k => k.StartsWith(r + "[", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Ticket P1-015 acceptance criterion 1: the keys <c>IL-COVERAGE.md</c> marks <c>lowered</c> are exactly the keys the
    /// lowering lowers, every key of P1-012's mapping table among them, and each names a test.
    /// </summary>
    [Fact]
    public void EveryLoweredKeyIsLoweredInTheTable()
    {
        ImmutableArray<string[]> rows =
        [
            .. File.ReadLines(Path.Combine(IlSamples.RepoRoot, "docs", "tickets", "IL-COVERAGE.md"))
                .Where(static l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| ILAst key", StringComparison.Ordinal))
                .Select(static l => l.Split('|')),
        ];
        ImmutableHashSet<string> lowered = [.. rows.Where(static r => r[2].TrimStart().StartsWith("lowered", StringComparison.Ordinal)).Select(static r => r[1].Trim())];

        Assert.Equal(IlKeys.Lowered.Order(StringComparer.Ordinal), lowered.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.All(rows.Where(r => lowered.Contains(r[1].Trim())), static r => Assert.Contains("Tests.", r[^3], StringComparison.Ordinal));
    }

    /// <summary>
    /// ADR 0024 decision 2: an opaque of an unmapped key that only reads locals has a fingerprint of what it does, the same
    /// for the same operation on other locals, with its reads and a shadow after each reference; one that takes
    /// a local's address, reads through a <c>ref</c> or a caught exception, or calls a runtime-changed member has none.
    /// </summary>
    [Fact]
    public void AnOpaqueThatOnlyReadsLocalsIsAFingerprintedFragment()
    {
        const string Members = "static int M(int a) => ~(a + a); static int N(int b) => ~(b + b); static int P(int a) => ~(a - a);"
            + " static int G(int a) => ~Id<int>(a); static T Id<T>(T t) => t; static int S(string s) => ~s.Length; int H() => ~GetHashCode();"
            + " static int R(string s) => ~s.IndexOf(\"x\"); static int W() { try { return Id(1); } catch (Exception e) { return ~e.HResult; } } static int T(int a) => ~Twice(ref a); static int Twice(ref int x) => x;"
            + " static int U(ref int x) => ~x;";
        Compilation compilation = Compile(Members);
        IrOpaque Fragment(string name) => Assert.Single(Opaques(IlLowerer.Lower(Method(compilation, name), compilation, Runtimes.Migration)));

        Assert.NotNull(Fragment("M").Fingerprint);
        Assert.Equal(Fragment("M").Fingerprint, Fragment("N").Fingerprint);
        Assert.NotEqual(Fragment("M").Fingerprint, Fragment("P").Fingerprint, StringComparer.Ordinal);
        Assert.Single(Fragment("M").Reads);
        Assert.NotNull(Fragment("G").Fingerprint);
        Assert.Equal([new IrSort("System.String"), new IrBool()], Fragment("S").Reads.Select(static r => r.Type));
        Assert.Equal(["this"], Fragment("H").Reads.Select(static r => r.Name), StringComparer.Ordinal);
        Assert.All(Unfingerprinted, n => Assert.Null(Fragment(n).Fingerprint));
    }

    private static readonly string[] Unfingerprinted = ["R", "W", "T", "U"];

    /// <summary>
    /// Ticket P2-055 (ADR 0040 decision 2): the IL lowering applies a runtime-change row only inside the pair's interval, as
    /// the IOperation lowering does. <c>String.IndexOf</c> changed in .NET 5, so on a .NET 8 to .NET 10 pair its call is not
    /// flagged and a fragment that calls it has a fingerprint, the same on both sides.
    /// </summary>
    [Theory]
    [InlineData("net48", "net10.0", true)]
    [InlineData("net8.0", "net10.0", false)]
    [InlineData("net10.0", "net10.0", false)]
    public void ARuntimeChangeAppliesOnlyInsideTheInterval(string legacy, string modern, bool changed)
    {
        Compilation compilation = Compile("static int R(string s) => ~s.IndexOf(\"x\"); static int Q(string s) => s.IndexOf(\"x\");");
        SideRuntime runtime = Runtimes.Between(legacy, modern);

        IrOpaque fragment = Assert.Single(Opaques(IlLowerer.Lower(Method(compilation, "R"), compilation, runtime)));
        IrCall call = Assert.Single(IlLowerer.Lower(Method(compilation, "Q"), compilation, runtime).Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>());

        Assert.Equal(changed, fragment.Fingerprint is null);
        Assert.Equal(changed, call.Callee.RuntimeChanged);
    }

    /// <summary>Ticket P2-055: the IL lowering marks a catalogued function runtime-sensitive as the side's runtime makes it.</summary>
    [Theory]
    [InlineData("net48", "net10.0", false, true, false)]
    [InlineData("net48", "net8.0", false, false, false)]
    [InlineData("net10.0", "net10.0", false, false, false)]
    [InlineData("net48", "net8.0", true, true, true)]
    public void APureFunctionIsRuntimeSensitiveAsTheSidesRuntimeMakesIt(string legacy, string modern, bool x87, bool conversion, bool arithmetic)
    {
        Compilation compilation = Compile("static int M(double a) => (int)a; static double N(double a) => a + a;");
        SideRuntime runtime = Runtimes.Between(legacy, modern, x87);
        IrPure Pure(string name) => Assert.Single(IlLowerer.Lower(Method(compilation, name), compilation, runtime).Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>());

        Assert.Equal(conversion, Pure("M").RuntimeSensitive);
        Assert.Equal(arithmetic, Pure("N").RuntimeSensitive);
    }

    /// <summary>The fingerprint's output writes what ILAst text never asks of an expression as plain markers.</summary>
    [Fact]
    public void TheFingerprintOutputWritesEveryPart()
    {
        IlFragment.Output output = new() { IndentationString = "  " };
        output.Indent();
        output.Unindent();
        output.Write('c');
        output.WriteLine();
        output.WriteReference(new ICSharpCode.Decompiler.Disassembler.OpCodeInfo(System.Reflection.Metadata.ILOpCode.Nop, "nop"));
        output.WriteReference(metadata: null!, default, "handle");
        output.MarkFoldStart();
        output.MarkFoldEnd();
        output.MarkDefinitionStart();

        Assert.Equal("{}c\nnophandle[]^", output.ToString());
        Assert.Equal("  ", output.IndentationString);
    }

    /// <summary>Types resolve through generic methods and types, nested types, arrays and async methods.</summary>
    [Theory]
    [InlineData("class C { static T M<T>(T t) { T x = t; return x; } }", "C")]
    [InlineData("class C<T> { T M(T t) { T x = t; return x; } }", "C`1")]
    [InlineData("class C { class N { int M(int[] a, System.Collections.Generic.List<int>.Enumerator e) => a.Length + e.Current; } }", "C+N")]
    [InlineData("class C { static async System.Threading.Tasks.Task<int> M() { await System.Threading.Tasks.Task.Yield(); return 1; } }", "C")]
    [InlineData("class C { static async System.Collections.Generic.IAsyncEnumerable<int> M() { await System.Threading.Tasks.Task.Yield(); yield return 1; } }", "C")]
    public void TypesResolveThroughGenericsNestingAndAsync(string source, string type)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        IMethodSymbol method = compilation.GetTypeByMetadataName(type)!.GetMembers("M").OfType<IMethodSymbol>().Single();

        Assert.Empty(IrValidator.Validate(IlLowerer.Lower(method, compilation, Runtimes.Migration)));
    }

    /// <summary>
    /// Ticket P1-015: a field write is its map written at the receiver, after the value and then the receiver's null check
    /// when the address delays its exceptions (a field of an object), and before the value when it does not (the struct
    /// field of an object the write goes through).
    /// </summary>
    [Fact]
    public void AFieldWriteIsItsMapWrite()
    {
        const string Members = "class K { public int F; public S St; } struct S { public int X; } static void M(K k, int v) { k.F = 10 / v; } static void N(K k, int v) { k.St.X = 10 / v; }";
        IrProcedure field = Lower(Members);
        IrProcedure nested = Lower(Members, "N");
        IrRun written = RunWith(field, ("k", new IrSortValue("C+K", 3)), ("v", IrBitVecValue.FromSigned(32, 2)));

        Assert.Contains(field.Blocks.SelectMany(static b => b.Instructions), static i => i is IrMapWrite { Target.Name: var name } && name.StartsWith("field.C_K.F", StringComparison.Ordinal));
        Assert.Equal(IrBitVecValue.FromSigned(32, 5), ((IrMapValue)Assert.Single(written.Outs)).Read(new IrSortValue("C+K", 3)));
        Assert.Equal(new IrThrew(PureCatalogue.DivideByZero), RunWith(field, ("k", new IrSortValue("C+K", 3)), ("null.C_K", Nulls("C+K", isNull: true))).Outcome);
        Assert.Equal(new IrThrew("System.NullReferenceException"), RunWith(nested, ("k", new IrSortValue("C+K", 3)), ("null.C_K", Nulls("C+K", isNull: true))).Outcome);
    }

    /// <summary>An address kept in a <c>ref</c> local escapes: its store is opaque, and so is every read through it.</summary>
    [Fact]
    public void AnEscapingAddressIsOpaque()
    {
        IrProcedure procedure = Lower("static int M(int[] a) { ref int r = ref a[0]; r++; return r; }");

        Assert.Contains(Opaques(procedure), static o => string.Equals(o.Reason, "StLoc[ref local]", StringComparison.Ordinal));
        Assert.Contains(Opaques(procedure), static o => string.Equals(o.Reason, "LdObj", StringComparison.Ordinal));
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("array.", StringComparison.Ordinal));
    }

    /// <summary>
    /// An element read checks the array for null first and its index against its length after, unsigned, so a negative
    /// index is out of range too (P2-017's order).
    /// </summary>
    [Fact]
    public void ArrayElementIsNullThenBoundsChecked()
    {
        IrProcedure procedure = Lower("static int M(int[] a, int i) => a[i];");
        IrValue array = new IrSortValue("int[]", 4);
        static (string, IrValue) Length(int length) => ("length.int__", new IrMapValue(new IrMap(new IrSort("int[]"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, length), []));
        (string, IrValue) Elements = ("array.int__", new IrMapValue(new IrMap(new IrSort("int[]"), new IrMap(new IrBitVec(32), new IrBitVec(32))), new IrMapValue(new IrMap(new IrBitVec(32), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 7), []), []));

        Assert.Equal(new IrThrew("System.NullReferenceException"), RunWith(procedure, ("a", array), ("i", IrBitVecValue.FromSigned(32, 5)), ("null.int__", Nulls("int[]", isNull: true))).Outcome);
        Assert.Equal(new IrThrew("System.IndexOutOfRangeException"), RunWith(procedure, ("a", array), ("i", IrBitVecValue.FromSigned(32, 5)), Length(3)).Outcome);
        Assert.Equal(new IrThrew("System.IndexOutOfRangeException"), RunWith(procedure, ("a", array), ("i", IrBitVecValue.FromSigned(32, -1)), Length(3)).Outcome);
        Assert.Equal(new IrReturned(IrBitVecValue.FromSigned(32, 7)), RunWith(procedure, ("a", array), ("i", IrBitVecValue.FromSigned(32, 1)), Length(3), Elements).Outcome);
    }

    /// <summary>
    /// A downcast throws <c>System.InvalidCastException</c> when its non-null operand fails the type test, passes a null
    /// one through, and is the operand's <c>cast</c> when it passes; <c>as</c> yields <c>null</c> when it fails.
    /// </summary>
    [Fact]
    public void DowncastThrowsInvalidCast()
    {
        const string Members = "static string M(object o) => (string)o; static string N(object o) => o as string;";
        IrProcedure downcast = Lower(Members);
        IrProcedure tryCast = Lower(Members, "N");
        static (string, IrValue) IsType(bool passes) => ("istype.System.Object.System.String", new IrMapValue(new IrMap(new IrSort("System.Object"), new IrBool()), new IrBoolValue(passes), []));
        (string, IrValue) cast = ("cast.System.Object.System.String", new IrMapValue(new IrMap(new IrSort("System.Object"), new IrSort("System.String")), new IrSortValue("System.String", 9), []));

        Assert.Equal(new IrThrew("System.InvalidCastException"), RunWith(downcast, IsType(passes: false)).Outcome);
        Assert.Equal(new IrReturned(new IrSortValue("System.String", 0)), RunWith(downcast, IsType(passes: false), ("null.System.Object", Nulls("System.Object", isNull: true))).Outcome);
        Assert.Equal(new IrReturned(new IrSortValue("System.String", 9)), RunWith(downcast, IsType(passes: true), cast).Outcome);
        Assert.Equal(new IrReturned(new IrSortValue("System.String", 0)), RunWith(tryCast, IsType(passes: false), cast).Outcome);
        Assert.Equal(new IrReturned(new IrSortValue("System.String", 9)), RunWith(tryCast, IsType(passes: true), cast).Outcome);
    }

    /// <summary>A <c>finally</c> runs on the <c>try</c>'s normal exit, on a <c>return</c> from it and on an exception out of it.</summary>
    [Theory]
    [InlineData(1, "return 1")]
    [InlineData(-1, "throw System.InvalidOperationException")]
    [InlineData(0, "return 2")]
    public void FinallyIsCopiedOnEveryExit(int a, string outcome)
    {
        IrProcedure procedure = Lower("static int M(int a) { try { if (a > 0) return 1; if (a < 0) throw new InvalidOperationException(); } finally { F(); } return 2; } static void F() { }");
        IrRun run = RunWith(procedure, ("a", IrBitVecValue.FromSigned(32, a)));

        Assert.Equal(outcome, Outcome(run.Outcome));
        Assert.Equal("C::F()", run.Trace[^1].Callee.Value);
        Assert.True(procedure.Blocks.SelectMany(static b => b.Instructions).Where(static i => i is IrCall { Callee.Value: "C::F()" }).Skip(2).Any());
    }

    /// <summary>A <c>catch</c> takes the exceptions of its type; a read of the exception it caught is opaque, as in the IOperation lowering.</summary>
    [Fact]
    public void ACaughtExceptionReadIsOpaque()
    {
        IrProcedure procedure = Lower("static int M(int a) { try { return 10 / a; } catch (DivideByZeroException e) { return e.HResult; } }");

        Assert.Contains(Opaques(procedure), static o => string.Equals(o.Reason, "LdLoc[caught exception]", StringComparison.Ordinal));
        Assert.IsType<IrOpaqueReached>(RunWith(procedure, ("a", IrBitVecValue.FromSigned(32, 0))).Outcome);
        Assert.Equal("return 5", Outcome(RunWith(procedure, ("a", IrBitVecValue.FromSigned(32, 2))).Outcome));
    }

    /// <summary>
    /// A <c>decimal</c> operator, which IL calls as <c>System.Decimal::op_*</c>, is the <see cref="PureCatalogue"/> function
    /// the IOperation lowering applies: arithmetic, comparison, negation, <c>++</c> and conversions, and <c>0m</c> and
    /// <c>1m</c>, which IL reads from <c>decimal</c>'s fields, are the constants.
    /// </summary>
    [Fact]
    public void DecimalOperatorIsItsCataloguedFunction()
    {
        Compilation compilation = Compile("static decimal M(decimal a, decimal b, int i) { a++; b--; return -a * b + +i / (a > b ? 0m : 1m) % 2.5m - (decimal)(double)a; }");
        IMethodSymbol method = Method(compilation, "M");
        static ImmutableArray<string> Functions(IrProcedure procedure) =>
            [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function).Order(StringComparer.Ordinal)];
        static ImmutableArray<IrValue> Constants(IrProcedure procedure) =>
            [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>().Select(static c => c.Value).Where(static v => v is IrSortValue).Distinct()];

        IrProcedure il = IlLowerer.Lower(method, compilation, Runtimes.Migration);
        IrProcedure operation = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);

        Assert.Empty(Opaques(il));
        Assert.Equal(Functions(operation), Functions(il), StringComparer.Ordinal);
        Assert.Equal(Constants(operation).ToHashSet(), [.. Constants(il)]);
    }

    /// <summary>
    /// A struct is a value: a field written through a copy's address, or through its auto-property's setter, makes a new
    /// value for the copy and never reaches the struct it was copied from; a field read of a struct value goes through a
    /// temporary.
    /// </summary>
    [Theory]
    [InlineData("M", 7 + 3)]
    [InlineData("N", 5)]
    [InlineData("P", 7)]
    [InlineData("Q", 7)]
    public void AStructCopyDoesNotAliasItsSource(string name, int result)
    {
        const string Members = "struct S { public int X; public int Y; } struct A { public int P { get; set; } }"
            + " static int M(S s) { S t = s; t.X = 5; return s.X + t.Y; } static int N(S s) { S t = s; t.X = 5; return t.X; }"
            + " static int P(S s) => Id(s).X; static S Id(S s) => s; static int Q(A a) { A c = a; c.P = 5; return a.P; }";
        IrProcedure procedure = Lower(Members, name);
        IrRun run = RunWith(
            procedure,
            new StructOracle(),
            ("field.C_S.X", new IrMapValue(new IrMap(new IrSort("C+S"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 7), [])),
            ("field.C_S.Y", new IrMapValue(new IrMap(new IrSort("C+S"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 3), [])),
            ("field.C_A.P", new IrMapValue(new IrMap(new IrSort("C+A"), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 7), [])),
            ("new.C_S", Fresh("C+S")),
            ("new.C_A", Fresh("C+A")));

        Assert.Empty(Opaques(procedure));
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"return {result}"), Outcome(run.Outcome));
    }

    /// <summary>
    /// Floating-point and <c>decimal</c> operators, comparisons and conversions are the <see cref="PureCatalogue"/> functions
    /// the IOperation lowering applies to the same C#: <c>-x</c>, which ILSpy reads as <c>0.0 - x</c>, is <c>neg</c>, and
    /// C#'s <c>0.0 - x</c> a subtraction; every operator <c>System.Decimal</c> declares but <c>+</c>, which Roslyn never
    /// calls, is catalogued. (A narrow integer converted to floating point is converted from its stack type, <c>int</c> or
    /// <c>long</c>, which IL widens it to: the same value, under another function's name.)
    /// </summary>
    [Theory]
    [InlineData("static double M(double a, float f, long l, ulong ul, uint u) => -a + (0.0 - a) + (a - 1.5) + f * f / 1.5f - (-f) + (0f - f) + (double)ul + (double)u + (double)l + (float)a + a % 2.0;")]
    [InlineData("static bool M(double a, float f) { bool x = a < 1.5; bool y = a >= 2.0; bool z = f > 0.5f; bool w = f <= 0f; return x & y & z & w; }")]
    [InlineData("static int M(double a, float f) => (int)a + checked((int)a) + (int)f + (byte)a + (int)(ulong)a;")]
    [InlineData("static decimal M(decimal a, decimal b, byte y, sbyte z, short s, ushort us, char c, int i, uint u, long l, ulong ul, float f, double d) { a++; b--; decimal r = -a + +b - a * b / (a % b); bool t = a == b || a != b || a < b || a <= b || a > b || a >= b; return r + y + z + s + us + c + i + u + l + ul + (decimal)f + (decimal)d + (t ? 1m : 0m); }")]
    [InlineData("static long M(decimal m) => (byte)m + (sbyte)m + (char)m + (short)m + (ushort)m + (int)m + (uint)m + (long)m + (long)(ulong)m + (long)(double)(float)m + (long)(double)m;")]
    public void FloatingPointAndDecimalAreTheOperationLoweringsFunctions(string members)
    {
        Compilation compilation = Compile(members);
        IMethodSymbol method = Method(compilation, "M");
        static ImmutableArray<string> Functions(IrProcedure procedure) =>
            [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrPure>().Select(static p => p.Function).Order(StringComparer.Ordinal)];

        IrProcedure il = IlLowerer.Lower(method, compilation, Runtimes.Migration);

        Assert.Empty(Opaques(il));
        Assert.Equal(Functions(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration)), Functions(il), StringComparer.Ordinal);
    }

    /// <summary>
    /// An address is its place where the IR has one: a <c>ref</c>, <c>out</c> or <c>in</c> parameter is its variable, written
    /// through with its null shadow; a call's <c>ref</c> and <c>out</c> arguments are its outputs, an <c>in</c> one the value
    /// at its address; a slot holding an element's address is that element, checked where it is taken.
    /// </summary>
    [Theory]
    [InlineData("static void M(ref int x, out int y, in int z) { y = x + z; x++; }", "x=4 y=7")]
    [InlineData("static int M(int a) { F(ref a); G(out int b); return a + b + In(a); } static void F(ref int x) { } static void G(out int y) { y = 1; } static int In(in int x) => x;", "refouts 1 1 0")]
    [InlineData("static string M(ref string s, string t) { s = t; G(out t); return t; } static void G(out string t) { t = null; }", "refouts 1")]
    [InlineData("static void M(ref int x) => F(ref x); static void F(ref int x) { }", "refouts 1")]
    [InlineData("static int M(int[] a) { a[0] += 1; return a[0]; }", "return 8")]
    [InlineData("static int M(int[] a, char[] c, bool[] b) { c[0] = 'x'; b[0] = c[0] == 'x'; return a[0] + (b[0] ? 1 : 0); }", "return 8")]
    public void AnAddressIsItsPlace(string members, string expected)
    {
        IrProcedure procedure = Lower(members);
        IrMapValue elements = new(new IrMap(new IrSort("int[]"), new IrMap(new IrBitVec(32), new IrBitVec(32))), new IrMapValue(new IrMap(new IrBitVec(32), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 7), []), []);
        IrRun run = RunWith(
            procedure,
            ("x", IrBitVecValue.FromSigned(32, 3)),
            ("z", IrBitVecValue.FromSigned(32, 4)),
            ("array.int__", elements),
            ("length.int__", Lengths("int[]")),
            ("length.char__", Lengths("char[]")),
            ("length.bool__", Lengths("bool[]")));
        ImmutableArray<IrCall> calls = [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>()];
        string actual = expected[..3] switch
        {
            "x=4" => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"x={((IrBitVecValue)run.Outs[0]).TwosComplement} y={((IrBitVecValue)run.Outs[1]).TwosComplement}"),
            "ref" => $"refouts {string.Join(' ', calls.Select(static c => c.RefOuts.Length))}",
            _ => Outcome(run.Outcome),
        };

        Assert.Empty(Opaques(procedure));
        Assert.Equal(expected, actual);
        Assert.DoesNotContain(procedure.Parameters, static p => p.Var.Name.StartsWith("cast.", StringComparison.Ordinal));
    }

    /// <summary>
    /// An address is opaque where the IR has no place: an element of a many-dimensional array or at a native index, a
    /// tuple's element, a <c>ref</c> field, a struct's <c>this</c>, a <c>ref</c> returned, and an element passed by
    /// <c>ref</c>, which the call's outputs cannot write.
    /// </summary>
    [Theory]
    [InlineData("static int M(int[,] a) { a[0, 1] += 1; return a[0, 1]; }", "LdElema[address operand]")]
    [InlineData("static int M(int[] a, nint i) => a[i];", "LdElema[address operand]")]
    [InlineData("static int M((int, int) t) => t.Item1;", "LdFlda[address operand]")]
    [InlineData("ref struct R { public ref int F; } static int M(R r) => r.F;", "LdObj")]
    [InlineData("struct S { public int X; public int Y() => X; } static int M(S s) => s.Y();", null)]
    [InlineData("static ref int M(ref int x) => ref x;", "LdLoc")]
    [InlineData("static void M(int[] a) => F(ref a[0]); static void F(ref int x) { }", "Call")]
    public void AnAddressWithNoPlaceIsOpaque(string members, string? reason)
    {
        Compilation compilation = Compile(members);
        IMethodSymbol method = reason is null ? compilation.GetTypeByMetadataName("C+S")!.GetMembers("Y").OfType<IMethodSymbol>().Single() : Method(compilation, "M");

        IrProcedure procedure = IlLowerer.Lower(method, compilation, Runtimes.Migration);

        Assert.Empty(IrValidator.Validate(procedure));
        Assert.Contains(Opaques(procedure), o => string.Equals(o.Reason, reason ?? "LdFlda[address operand]", StringComparison.Ordinal));
    }

    /// <summary>
    /// A named method's pointer is the element of its call identity, for an instance method, a virtual one after its
    /// receiver's null check, and a static one; a lambda's is opaque.
    /// </summary>
    [Fact]
    public void AFunctionPointerNamesItsMethod()
    {
        const string Members = "int N() => 1; static int S() => 1; Func<int> M() => N; static Func<string> V(object o) => o.ToString; static Func<int> P() => new Func<int>(S);";
        Compilation compilation = Compile(Members);
        INamedTypeSymbol intPtr = compilation.GetSpecialType(SpecialType.System_IntPtr);
        IrValue Named(string name) => TypeMapper.Constant(intPtr, CallIdentityFactory.Of(name.Contains('.', StringComparison.Ordinal) ? compilation.GetSpecialType(SpecialType.System_Object).GetMembers("ToString").OfType<IMethodSymbol>().Single() : Method(compilation, name), compilation, RenameMap.Empty, [], Runtimes.Migration.Interval).Value);
        IrProcedure instance = Lower(Members);
        IrProcedure @virtual = Lower(Members, "V");

        Assert.Contains(instance.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>(), c => c.Value == Named("N"));
        Assert.Contains(@virtual.Blocks.SelectMany(static b => b.Instructions).OfType<IrConst>(), c => c.Value == Named("object.ToString"));
        Assert.Equal(new IrThrew("System.NullReferenceException"), RunWith(@virtual, ("null.System.Object", Nulls("System.Object", isNull: true))).Outcome);
        Assert.DoesNotContain(Opaques(Lower(Members, "P")), static o => o.Reason.StartsWith("LdFtn", StringComparison.Ordinal));
    }

    /// <summary>
    /// A type test of an operand already of the tested type passes on every non-null operand, with no <c>istype</c> read;
    /// one of an operand that cannot be null reads only <c>istype</c>; one of unrelated types is opaque.
    /// </summary>
    [Fact]
    public void ATypeTestReadsOnlyWhatItNeeds()
    {
        const string Members = "class B { } class D : B { } static D M() => (D)(B)new D(); static D N() => (B)new D() as D; static D P() => (D)new B(); static D Q() => new B() as D; static string R(Exception e) => (string)(object)e;";

        Assert.DoesNotContain(Lower(Members).Parameters, static p => p.Var.Name.StartsWith("istype.", StringComparison.Ordinal));
        Assert.DoesNotContain(Lower(Members, "N").Parameters, static p => p.Var.Name.StartsWith("istype.", StringComparison.Ordinal));
        Assert.Equal(new IrThrew("System.InvalidCastException"), RunWith(Lower(Members, "P")).Outcome);
        Assert.Equal(new IrReturned(new IrSortValue("C+D", 0)), RunWith(Lower(Members, "Q")).Outcome);
        Assert.Contains(Opaques(Lower(Members, "R")), static o => string.Equals(o.Reason, "CastClass", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>new T[n]</c> throws on a negative length unless the length is a constant, and <c>Length</c> reads the length map;
    /// a creation of several dimensions, by a native length, of arrays, or of a struct with no default is opaque.
    /// </summary>
    [Theory]
    [InlineData("static int M(int n) => new int[n].Length + new int[3].Length;", 2, "return 5")]
    [InlineData("static int M(int n) => new int[n].Length + new int[3].Length;", -1, "throw System.OverflowException")]
    [InlineData("static int M(long n) => new int[n].Length;", 0, "NewArr")]
    [InlineData("static int M(int n) => new int[n][].Length;", 0, "NewArr")]
    [InlineData("static int M(int n) => new DateTime[n].Length;", 0, "NewArr")]
    public void AnArrayIsCreatedAndMeasured(string members, int n, string expected)
    {
        IrProcedure procedure = Lower(members);
        ImmutableArray<IrOpaque> opaques = Opaques(procedure);
        string actual = opaques.IsEmpty
            ? Outcome(RunWith(procedure, ("n", IrBitVecValue.FromSigned(32, n)), ("new.int__", Fresh("int[]"))).Outcome)
            : opaques.Select(static o => o.Reason).First(static r => r.StartsWith("NewArr", StringComparison.Ordinal));

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A box is its value's <c>cast</c> to the reference type it is used as (ticket M3-010), and <c>typeof</c> the shared
    /// input, never null; a box of a type parameter or a nullable, and <c>typeof</c> of a type parameter or a pointer, are opaque.
    /// </summary>
    [Theory]
    [InlineData("static object M(int a) => a;", "cast.System.Int32.System.Object")]
    [InlineData("static IComparable M(int a) => a;", "cast.System.Int32.System.IComparable")]
    [InlineData("static int M() => typeof(string).Name.Length;", "typeof.System.String")]
    [InlineData("static object M<T>(T t) => t;", "Box")]
    [InlineData("static object M(int? a) => a;", "Box")]
    [InlineData("static Type M<T>() => typeof(T);", "LdTypeToken")]
    [InlineData("static unsafe Type M() => typeof(int*);", "LdTypeToken")]
    public void ABoxAndATypeAreTheirInputs(string members, string expected)
    {
        IrProcedure procedure = Lower(members);

        Assert.Contains(expected, procedure.Parameters.Select(static p => p.Var.Name).Concat(Opaques(procedure).Select(static o => o.Reason)), StringComparer.Ordinal);
        Assert.DoesNotContain(procedure.Parameters, static p => string.Equals(p.Var.Name, "null.System.Type", StringComparison.Ordinal));
    }

    /// <summary>
    /// An exception goes to the first handler whose type it converts to: past a <c>when</c> filter that declines, or that
    /// throws, to the next; a known type never to a later handler once one takes it; one of no known type that two handlers
    /// could take is <c>call-throw-in-try</c>, as the IOperation lowering routes it.
    /// </summary>
    [Theory]
    [InlineData(0, 1, "return 2")]
    [InlineData(0, 9, "return 3")]
    [InlineData(0, 0, "return 3")]
    [InlineData(2, 1, "return 5")]
    public void AFilterDeclinesToTheNextHandler(int a, int b, string expected)
    {
        IrProcedure procedure = Lower("static int M(int a, int b) { try { return 10 / a; } catch (DivideByZeroException) when (10 / b > 1) { return 2; } catch (ArithmeticException) { return 3; } catch (Exception) { return 4; } }");

        Assert.Equal(expected, Outcome(RunWith(procedure, ("a", IrBitVecValue.FromSigned(32, a)), ("b", IrBitVecValue.FromSigned(32, b))).Outcome));
        Assert.Contains(Opaques(Lower("static int M(int a) { try { return F(a); } catch (InvalidOperationException) { return 2; } catch (ArgumentException) { return 3; } } static int F(int a) => a;")), static o => string.Equals(o.Reason, "call-throw-in-try", StringComparison.Ordinal));
    }

    /// <summary>
    /// A <c>callvirt</c> names the override the receiver's static type binds, as C# and the IOperation lowering do, generic
    /// ones constructed as the call is; a constructor's call of its base is a call with <c>this</c>; a thrown object whose
    /// constructor is refused is opaque, and so is where the method goes after it.
    /// </summary>
    [Fact]
    public void ACallNamesTheOverrideItBinds()
    {
        const string Members = "class B { public virtual int M<T>(T t) => 1; public virtual int N() => 2; } class D : B { public override int M<T>(T t) => 3; public override int N() => 4; }"
            + " static int P(D d) => d.M(1) + d.N() + d.GetHashCode(); int f; C(int a) { f = a; }"
            + " class E : Exception { public E(ref int a, ref int b) { } } static void T(int a) => throw new E(ref a, ref a);";
        Compilation compilation = Compile(Members);
        static ImmutableArray<string> Calls(IrProcedure procedure) => [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>().Select(static c => c.Callee.Value).Order(StringComparer.Ordinal)];
        IMethodSymbol method = Method(compilation, "P");

        Assert.Equal(Calls(IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration)), Calls(IlLowerer.Lower(method, compilation, Runtimes.Migration)), StringComparer.Ordinal);
        Assert.Contains("System.Object::.ctor()", Calls(Lower(Members, ".ctor")), StringComparer.Ordinal);
        Assert.Contains(Opaques(Lower(Members, "T")), static o => string.Equals(o.Reason, "Throw[new]", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reference receiver of an interface's method is converted to the interface through its <c>cast</c> map, as C#'s
    /// conversion is, unless it already is one; a struct's, called through <c>constrained.</c>, is the struct's value.
    /// </summary>
    [Theory]
    [InlineData("static void M(IDisposable d) => d.Dispose();", false)]
    [InlineData("static void M(System.IO.StringWriter w) => ((IDisposable)w).Dispose();", true)]
    [InlineData("static void M(System.Collections.Generic.List<int> l) { using (var e = l.GetEnumerator()) { } }", false)]
    public void AnInterfacesReceiverIsOfTheInterface(string members, bool cast)
    {
        IrProcedure procedure = Lower(members);

        Assert.Empty(Opaques(procedure));
        Assert.Contains(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrCall>(), static c => string.Equals(c.Callee.Value, "System.IDisposable::Dispose()", StringComparison.Ordinal));
        Assert.Equal(cast, procedure.Parameters.Any(static p => p.Var.Name.StartsWith("cast.", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A member of a reference that did not load is refused, never an exception: a field, a struct's default, box, array
    /// and element, a type test, a method's pointer, a <c>catch</c> of its type, and <c>typeof</c> of it.
    /// </summary>
    [Theory]
    [InlineData("int M(S s) => s.X;", "LdFlda[address operand]")]
    [InlineData("S M() => default;", "DefaultValue")]
    [InlineData("object M(S s) => s;", "Box")]
    [InlineData("int M() => new S[2].Length;", "NewArr")]
    [InlineData("int M(S[] a) => a[0].X;", "LdElema[address operand]")]
    [InlineData("object M(object o) => (D)o;", "CastClass")]
    [InlineData("Func<int> M(K k) => k.N;", "LdFtn")]
    [InlineData("Func<int> M(K k) => k.V;", "LdVirtFtn")]
    [InlineData("int M(int a) { try { return F(a); } catch (E) { return 2; } } static int F(int a) => a;", "TryCatch")]
    [InlineData("Type M() => typeof(S);", "Call")]
    public void AnUnloadedReferenceIsRefused(string members, string reason)
    {
        Compilation library = RoslynTestCompilations.Compile(
            "public struct S { public int X; } public class E : System.Exception { } public class K { public int N() => 1; public virtual int V() => 2; } public class B { } public class D : B { }",
            "Unloaded");
        string path = Path.Combine(Path.GetTempPath(), $"equiv-il-{Guid.NewGuid():N}.dll");
        using (MemoryStream image = new())
        {
            Assert.True(library.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
            File.WriteAllBytes(path, image.ToArray());
        }

        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{members}\n}}\n", [MetadataReference.CreateFromFile(path)]);
        File.Delete(path);

        IrProcedure procedure = IlLowerer.Lower(Method(compilation, "M"), compilation, Runtimes.Migration);

        Assert.Empty(IrValidator.Validate(procedure));
        Assert.Contains(Opaques(procedure), o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
    }

    /// <summary>Compiles <paramref name="members"/> in class <c>C</c>, then runs <c>M</c> by reflection and its IL lowering by the interpreter.</summary>
    internal static void Agree(string members, params object[] arguments) => Agree(members, "M", arguments);

    internal static void Agree(string members, string name, params object[] arguments)
    {
        Compilation compilation = Compile(members);
        IrProcedure procedure = IlLowerer.Lower(Method(compilation, name), compilation, Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        using MemoryStream image = new();
        Assert.True(compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        image.Position = 0;
        System.Runtime.Loader.AssemblyLoadContext context = new("il-lowering-agree", isCollectible: true);
        try
        {
            System.Reflection.MethodInfo method = context.LoadFromStream(image).GetType("C")!.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            string expected;
            try
            {
                expected = $"return {Bits(method.Invoke(null, arguments)!)}";
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                expected = $"throw {exception.InnerException!.GetType().FullName}";
            }

            string actual = Run(procedure, [.. arguments.Select(Bits)]) switch
            {
                IrReturned { Value: { } value } => $"return {value}",
                IrThrew thrown => $"throw {thrown.ExceptionType}",
                var other => other.ToString(),
            };
            Assert.Equal(expected, actual);
        }
        finally
        {
            context.Unload();
        }
    }

    private static IrValue Bits(object value) => value switch
    {
        int i => IrBitVecValue.FromSigned(32, i),
        _ => IrBitVecValue.FromSigned(64, (long)value),
    };

    private static Compilation Compile(string members)
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{members}\n}}\n");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private static IMethodSymbol Method(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single();

    internal static IrProcedure Lower(string members, string name = "M")
    {
        Compilation compilation = RoslynTestCompilations.Compile($"using System;\nclass C\n{{\n{members}\n}}\n");
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IrProcedure procedure = IlLowerer.Lower(compilation.GetTypeByMetadataName("C")!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation, Runtimes.Migration);
        Assert.Empty(IrValidator.Validate(procedure));
        return procedure;
    }

    /// <summary>
    /// Runs <paramref name="procedure"/> with the inputs <paramref name="named"/> names, and every other one its default: a
    /// bitvector 0, <c>false</c>, element 1 of a sort, and a map from everything to its value's default, so no reference is
    /// null.
    /// </summary>
    internal static IrRun RunWith(IrProcedure procedure, params (string Name, IrValue Value)[] named) => RunWith(procedure, new StructOracle(), named);

    private static IrRun RunWith(IrProcedure procedure, ICallOracle oracle, params (string Name, IrValue Value)[] named)
    {
        Dictionary<string, IrValue> values = named.ToDictionary(static n => n.Name, static n => n.Value, StringComparer.Ordinal);
        return IrInterpreter.Run(
            procedure,
            new IrInputs([.. procedure.Parameters.Select(p => values.GetValueOrDefault(p.Var.Name) ?? Default(p.Var.Type))]),
            oracle,
            IrGen.StepBudget,
            pure: IrGenOracle.Instance);
    }

    private static IrValue Default(IrType type) => type switch
    {
        IrBitVec bits => new IrBitVecValue(bits.Width, 0),
        IrBool => new IrBoolValue(Value: false),
        IrSort sort => new IrSortValue(sort.Name, 1),
        _ => new IrMapValue((IrMap)type, Default(((IrMap)type).Value), []),
    };

    /// <summary>Every array of <paramref name="sort"/> one element long.</summary>
    private static IrMapValue Lengths(string sort) =>
        new(new IrMap(new IrSort(sort), new IrBitVec(32)), IrBitVecValue.FromSigned(32, 1), []);

    /// <summary>Every value of <paramref name="sort"/> null, or none.</summary>
    private static IrMapValue Nulls(string sort, bool isNull) =>
        new(new IrMap(new IrSort(sort), new IrBool()), new IrBoolValue(isNull), []);

    /// <summary>A body's k-th new value of <paramref name="sort"/> is element 100 + k, none of which an input holds.</summary>
    private static IrMapValue Fresh(string sort) =>
        new(new IrMap(new IrBitVec(32), new IrSort(sort)), new IrSortValue(sort, 99), Enumerable.Range(0, 8).ToImmutableDictionary(static k => (IrValue)new IrBitVecValue(32, (ulong)k), k => (IrValue)new IrSortValue(sort, 100 + k)));

    private static string Outcome(IrOutcome outcome) => outcome switch
    {
        IrReturned { Value: IrBitVecValue bits } => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"return {bits.TwosComplement}"),
        IrThrew thrown => $"throw {thrown.ExceptionType}",
        var other => other.ToString()!,
    };

    internal static IrOutcome Run(IrProcedure procedure, params IrValue[] arguments) =>
        IrInterpreter.Run(procedure, new IrInputs([.. arguments]), IrGenOracle.Instance, IrGen.StepBudget).Outcome;

    internal static ImmutableArray<IrOpaque> Opaques(IrProcedure procedure) =>
        [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];

    /// <summary>An instruction with an unmapped key that no instruction with an unmapped key holds.</summary>
    private static bool Outermost(ILInstruction instruction, HashSet<ILVariable> caught) =>
        !IlKeys.Lowered.Contains(IlKeys.Key(instruction, caught))
        && !instruction.Ancestors.Skip(1).Any(a => !IlKeys.Lowered.Contains(IlKeys.Key(a, caught)));

    /// <summary>
    /// Each path's callees in order, as a sorted set of sequences, so that two lowerings that lay out the same branches
    /// differently (ILSpy may negate a condition and swap its arms) agree; then each parameter's name and sort. A path is cut
    /// where it would enter a block it has already entered.
    /// </summary>
    private static string Signature(IrProcedure procedure)
    {
        Dictionary<IrBlockId, IrBlock> blocks = procedure.Blocks.ToDictionary(static b => b.Id);
        SortedSet<string> paths = new(StringComparer.Ordinal);
        void Walk(IrBlockId id, ImmutableList<string> calls, ImmutableHashSet<IrBlockId> entered)
        {
            IrBlock block = blocks[id];
            ImmutableList<string> through = calls.AddRange(block.Instructions.OfType<IrCall>().Select(static c => c.Callee.Value));
            ImmutableArray<IrBlockId> next = [.. Successors(block.Terminator).Where(s => !entered.Contains(s))];
            if (next.IsEmpty)
            {
                paths.Add(string.Join(" > ", through));
            }

            foreach (IrBlockId successor in next)
            {
                Walk(successor, through, entered.Add(successor));
            }
        }

        Walk(procedure.Entry, [], [procedure.Entry]);
        return $"calls {{{string.Join(" | ", paths)}}} parameters [{string.Join(", ", procedure.Parameters.Select(static p => $"{p.Var.Name}: {p.Var.Type}"))}]";
    }

    private static IEnumerable<IrBlockId> Successors(IrTerminator terminator) => terminator switch
    {
        IrGoto jump => [jump.Target],
        IrBranch branch => [branch.Then, branch.Else],
        IrSwitch choice => [.. choice.Cases.Select(static c => c.Target), choice.Default],
        _ => [],
    };

    internal static string Text(ILFunction function)
    {
        using PlainTextOutput output = new();
        function.WriteTo(output, new ILAstWritingOptions());
        return output.ToString();
    }

    /// <summary>A callee that never throws and returns its first argument when it is of the result's type, as <c>Id</c> does; otherwise <see cref="IrGenOracle"/>'s value.</summary>
    private sealed class StructOracle : ICallOracle
    {
        public IrCallResult Answer(CallIdentity callee, ImmutableArray<IrValue> arguments, IrType? resultType, int position, ImmutableArray<IrHeapSlice> heap, ImmutableArray<IrType> refOuts)
        {
            IrCallResult answer = IrGenOracle.Instance.Answer(callee, arguments, resultType, position, heap, refOuts);
            return answer with { Value = arguments is [{ } first, ..] && first.Type == resultType ? first : answer.Value, Threw = false };
        }
    }
}
