using System.Collections.Immutable;

using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>
/// Ticket P2-079: a fragment whose text names a compiler-generated method or type has no fingerprint. Such a name is an
/// ordinal and the code it names is elsewhere, so two fragments that differ only in a lambda's or a local function's body
/// had one fingerprint, were shared as one call (ADR 0024 decision 2), and the pair proved.
/// </summary>
public sealed class IlFragmentTests
{
    /// <summary>The ticket's repro: <c>{0}</c> is the lambdas' addend, 1 on the legacy side and 2 on the modern one.</summary>
    private const string Holder = """
        using System;
        public sealed class Holder
        {
            private int _x;
            public Lazy<int> Value { get; private set; }
            public int X
            {
                get => _x;
                set { _x = value; Value = new Lazy<int>(() => _x + {0}); }
            }
            public void Subscribe(Action<Func<int>> register)
            {
                register(() => _x + {0});
            }
        }
        """;

    private const string StaticGroup = "static int M() => ~Apply(new Func<char, bool>(char.IsLetter)); static int Apply(Func<char, bool> f) => 0;";

    private const string VirtualGroup = "static int M(System.Text.Encoding e) => ~Apply(new Func<byte[], string>(e.GetString)); static int Apply(Func<byte[], string> f) => 0;";

    /// <summary>Acceptance criterion 1: neither member of the repro has a fragment whose fingerprint the other side has too.</summary>
    [Theory]
    [InlineData("set_X")]
    [InlineData("Subscribe")]
    public void AFragmentThatNamesALambdaHasNoFingerprint(string member)
    {
        ImmutableArray<IrOpaque> legacy = Opaques(Holder.Replace("{0}", "1", StringComparison.Ordinal), "Holder", member);
        ImmutableArray<IrOpaque> modern = Opaques(Holder.Replace("{0}", "2", StringComparison.Ordinal), "Holder", member);

        Assert.Contains(legacy, static o => string.Equals(o.Reason, "LdFtn[lambda]", StringComparison.Ordinal));
        Assert.All(legacy.Concat(modern), static o => Assert.Null(o.Fingerprint));
    }

    /// <summary>
    /// Acceptance criterion 2, each way a fragment names generated code: a lambda that captures only <c>this</c> (a method
    /// of the class), one that captures nothing (a method and a cached delegate of <c>&lt;&gt;c</c>), one that captures a
    /// local (a closure class), an <c>async</c> lambda (a state machine), a method group the compiler caches, and an
    /// anonymous type.
    /// </summary>
    [Theory]
    [InlineData("int f; Func<int> M() => () => f + 1;")]
    [InlineData("static Func<int> M() => () => 1;")]
    [InlineData("static Func<int> M(int a) => () => a + 1;")]
    [InlineData("static int M(int a) => ~Apply(() => a + 1); static int Apply(Func<int> f) => f();")]
    [InlineData("static Func<System.Threading.Tasks.Task<int>> M() => async () => await System.Threading.Tasks.Task.FromResult(1);")]
    [InlineData("static Func<int> M() => S; static int S() => 1;")]
    [InlineData("static int M(int a) => ~new { A = a }.A;")]
    public void AFragmentThatNamesGeneratedCodeHasNoFingerprint(string members)
    {
        ImmutableArray<IrOpaque> opaques = Opaques(InC(members), "C", "M");

        Assert.NotEmpty(opaques);
        Assert.All(opaques, static o => Assert.Null(o.Fingerprint));
    }

    /// <summary>A call of a local function, and a method group of one, are in no fingerprinted fragment.</summary>
    [Theory]
    [InlineData("static int M(int a) { return L(a); int L(int v) => v + 1; }", "Call[local function]")]
    [InlineData("static int M(int a) { return ~L(a); static int L(int v) => v + 1; }", "BitNot")]
    [InlineData("static Func<int, int> M() { return new Func<int, int>(L); static int L(int v) => v + 1; }", "LdFtn[lambda]")]
    public void AFragmentThatNamesALocalFunctionHasNoFingerprint(string members, string reason)
    {
        ImmutableArray<IrOpaque> opaques = Opaques(InC(members), "C", "M");

        Assert.Contains(opaques, o => string.Equals(o.Reason, reason, StringComparison.Ordinal));
        Assert.All(opaques, static o => Assert.Null(o.Fingerprint));
    }

    /// <summary>
    /// What the rule leaves shared: a fragment that names a field, a property's backing field (whose name the compiler
    /// writes, but from the property's), or a method by its own name keeps its fingerprint.
    /// </summary>
    [Theory]
    [InlineData("int f; int M() => ~f;", "M")]
    [InlineData("int P { get => ~field; set; }", "get_P")]
    [InlineData("int M() => ~GetHashCode();", "M")]
    [InlineData("static int M(int a) => ~Id<int>(a); static T Id<T>(T t) => t;", "M")]
    public void AFragmentThatNamesOnlyDeclaredMembersKeepsItsFingerprint(string members, string member) =>
        Assert.NotNull(Assert.Single(Opaques(InC(members), "C", member)).Fingerprint);

    /// <summary>
    /// Acceptance criterion 3 inside a fragment: one that holds a method group of a member <c>runtime-changes.json</c> names
    /// has no fingerprint on a pair whose runtimes the change lies between, as one that calls the member has none (M3-015).
    /// </summary>
    [Theory]
    [InlineData(StaticGroup, "net48", "net10.0", true)]
    [InlineData(StaticGroup, "net10.0", "net10.0", false)]
    [InlineData(VirtualGroup, "net48", "net10.0", true)]
    [InlineData(VirtualGroup, "net10.0", "net10.0", false)]
    public void AFragmentThatHoldsARuntimeChangedMethodGroupHasNoFingerprint(string members, string legacy, string modern, bool changed)
    {
        IrOpaque fragment = Assert.Single(Opaques(InC(members), "C", "M", Runtimes.Between(legacy, modern)));

        Assert.Equal(changed, fragment.Fingerprint is null);
    }

    /// <summary>A fragment that calls a member of a reference that did not load, which resolves to no symbol, has no fingerprint.</summary>
    [Fact]
    public void AFragmentThatCallsAnUnresolvedMemberHasNoFingerprint()
    {
        Compilation library = RoslynTestCompilations.Compile("public static class K { public static int S() => 1; }", "Unloaded");
        string path = Path.Combine(Path.GetTempPath(), $"equiv-il-{Guid.NewGuid():N}.dll");
        using (MemoryStream image = new())
        {
            Assert.True(library.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
            File.WriteAllBytes(path, image.ToArray());
        }

        Compilation compilation = RoslynTestCompilations.Compile(InC("static int M() => ~K.S();"), [MetadataReference.CreateFromFile(path)]);
        File.Delete(path);

        IrProcedure procedure = IlLowerer.Lower(compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single(), compilation, Runtimes.Migration);

        Assert.Null(Assert.Single(procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()).Fingerprint);
    }

    private static string InC(string members) => $"using System;\nclass C\n{{\n{members}\n}}\n";

    private static ImmutableArray<IrOpaque> Opaques(string source, string type, string member) => Opaques(source, type, member, Runtimes.Migration);

    private static ImmutableArray<IrOpaque> Opaques(string source, string type, string member, Equiv.Frontend.CSharp.Lowering.SideRuntime runtime)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        IrProcedure procedure = IlLowerer.Lower(compilation.GetTypeByMetadataName(type)!.GetMembers(member).OfType<IMethodSymbol>().Single(), compilation, runtime);
        Assert.Empty(IrValidator.Validate(procedure));
        return [.. procedure.Blocks.SelectMany(static b => b.Instructions).OfType<IrOpaque>()];
    }
}
