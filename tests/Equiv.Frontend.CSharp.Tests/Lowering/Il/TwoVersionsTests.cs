using System.Collections.Frozen;
using System.Globalization;

using Equiv.Frontend.CSharp.Lowering.Il;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering.Il;

/// <summary>
/// ADR 0061 (ticket P2-179): whether the two callees of a rebound pair, one assembly's members at two versions, run the same
/// IL. Each case builds the library twice, as a file the way a package is restored, and an application that references one
/// build on each side; <c>Lib.C::F</c> is the callee unless a case says otherwise.
/// </summary>
public sealed class TwoVersionsTests
{
    private const string Same = "public static class C { public static int F(int a) => a + 1; }";

    /// <summary>Each case: the legacy library's source, the modern one's, and whether <c>Lib.C::F</c> is one call in both.</summary>
    private static readonly FrozenDictionary<string, (string Legacy, string Modern, bool Same)> Cases = new Dictionary<string, (string, string, bool)>(StringComparer.Ordinal)
    {
        ["the same body, with framework calls and a switch"] = (
            "public static class C { public static string F(string s, int n) { switch (n) { case 0: return s.ToUpperInvariant(); case 1: return s + \"!\"; default: return s.Trim(); } } }",
            "public static class C { public static string F(string s, int n) { switch (n) { case 0: return s.ToUpperInvariant(); case 1: return s + \"!\"; default: return s.Trim(); } } }",
            true),
        ["another string"] = ("public static class C { public static string F(string s) => s + \"!\"; }", "public static class C { public static string F(string s) => s + \"?\"; }", false),
        ["another opcode"] = ("public static class C { public static int F(int a, int b) => a + b; }", "public static class C { public static int F(int a, int b) => a - b; }", false),
        ["another immediate"] = ("public static class C { public static int F(int a) => a + 10; }", "public static class C { public static int F(int a) => a + 20; }", false),
        ["another length"] = (Same, "public static class C { public static int F(int a) => a + 1 + a; }", false),
        ["another framework member"] = (
            "public static class C { public static string F(string s) => s.ToUpperInvariant(); }",
            "public static class C { public static string F(string s) => s.ToLowerInvariant(); }",
            false),
        ["a member a runtime-changes row names"] = (
            "public static class C { public static string F(string a, string b) => System.IO.Path.Combine(a, b); }",
            "public static class C { public static string F(string a, string b) => System.IO.Path.Combine(a, b); }",
            false),
        ["a member of a type a runtime-changes row names"] = (
            "public static class C { public static int F(string a, string b) => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(a, b); }",
            "public static class C { public static int F(string a, string b) => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(a, b); }",
            false),
        ["a framework virtual no method of the library may override"] = (
            "public static class C { public static int F(object o) => o.GetHashCode(); }",
            "public static class C { public static int F(object o) => o.GetHashCode(); }",
            true),
        ["a framework virtual a method of the library may override"] = (
            "public static class C { public static string F(object o) => o.ToString(); } public class D { public override string ToString() => \"d\"; }",
            "public static class C { public static string F(object o) => o.ToString(); } public class D { public override string ToString() => \"d\"; }",
            false),
        ["a framework virtual only the modern library may override"] = (
            "public static class C { public static string F(object o) => o.ToString(); }",
            "public static class C { public static string F(object o) => o.ToString(); } public class D { public override string ToString() => \"d\"; }",
            false),
        ["a delegate to a framework virtual"] = (
            "public static class C { public static System.Func<int> F(object o) => o.GetHashCode; }",
            "public static class C { public static System.Func<int> F(object o) => o.GetHashCode; }",
            true),
        ["another signature"] = ("public static class C { public static object F(object o) => o; }", "public static class C { public static string F(string o) => o; }", false),
        ["other local types"] = (
            "public static class C { public static object F(string s) { object o = s; return o; } }",
            "public static class C { public static object F(string s) { string o = s; return o; } }",
            false),
        ["other implementation flags"] = (
            "public static class C { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public static int F(int a) => a + 1; }",
            Same,
            false),
        ["another caught type"] = (
            "public static class C { public static int F(string s) { try { return int.Parse(s); } catch (System.FormatException) { return 0; } } }",
            "public static class C { public static int F(string s) { try { return int.Parse(s); } catch (System.OverflowException) { return 0; } } }",
            false),
        ["the same catch"] = (
            "public static class C { public static int F(string s) { try { return int.Parse(s); } catch (System.FormatException) { return 0; } } }",
            "public static class C { public static int F(string s) { try { return int.Parse(s); } catch (System.FormatException) { return 0; } } }",
            true),
        ["the same finally and helper"] = (
            "public static class C { public static int F(string s) { try { return s.Length; } finally { G(); } } static void G() { } }",
            "public static class C { public static int F(string s) { try { return s.Length; } finally { G(); } } static void G() { } }",
            true),
        ["a caught type of the library whose base changed"] = (
            "public class E : System.Exception { } public static class C { public static int F() { try { G(); return 1; } catch (E) { return 0; } } static void G() { } }",
            "public class E : System.InvalidOperationException { } public static class C { public static int F() { try { G(); return 1; } catch (E) { return 0; } } static void G() { } }",
            false),
        ["a helper that changed"] = (
            "public static class C { public static int F() => H(); static int H() => 1; }",
            "public static class C { public static int F() => H(); static int H() => 2; }",
            false),
        ["a helper renamed with the same body"] = (
            "public static class C { public static int F() => H(); static int H() => 1; }",
            "public static class C { public static int F() => K(); static int K() => 1; }",
            true),
        ["mutual recursion"] = (
            "public static class C { public static int F(int n) => n <= 0 ? 0 : G(n - 1); static int G(int n) => F(n) + 1; }",
            "public static class C { public static int F(int n) => n <= 0 ? 0 : G(n - 1); static int G(int n) => F(n) + 1; }",
            true),
        ["a virtual call into the library"] = (
            "public class B { public virtual int V() => 1; } public static class C { public static int F(B b) => b.V(); }",
            "public class B { public virtual int V() => 1; } public static class C { public static int F(B b) => b.V(); }",
            false),
        ["a non-virtual instance call into the library"] = (
            "public class B { public int V() => 1; } public static class C { public static int F(B b) => b.V(); }",
            "public class B { public int V() => 1; } public static class C { public static int F(B b) => b.V(); }",
            true),
        ["other type arguments to a library method"] = (
            "public static class C { public static int F() => H<int>(); static int H<T>() => 1; }",
            "public static class C { public static int F() => H<long>(); static int H<T>() => 1; }",
            false),
        ["other type arguments of a library type"] = (
            "public static class Box<T> { public static int Get() => 1; } public static class C { public static int F() => Box<int>.Get(); }",
            "public static class Box<T> { public static int Get() => 1; } public static class C { public static int F() => Box<long>.Get(); }",
            false),
        ["a static field whose initial value changed"] = (
            "public static class H { public static int K = 1; } public static class C { public static int F() => H.K; }",
            "public static class H { public static int K = 2; } public static class C { public static int F() => H.K; }",
            false),
        ["a static field no static constructor sets"] = (
            "public static class H { public static int K; } public static class C { public static int F() => H.K; }",
            "public static class H { public static int K; } public static class C { public static int F() => H.K; }",
            true),
        ["a static constructor added"] = (
            "public static class H { public static int K; } public static class C { public static int F() => H.K; }",
            "public static class H { public static int K; static H() { K = 0; } } public static class C { public static int F() => H.K; }",
            false),
        ["a static constructor removed"] = (
            "public static class H { public static int K; static H() { K = 0; } } public static class C { public static int F() => H.K; }",
            "public static class H { public static int K; } public static class C { public static int F() => H.K; }",
            false),
        ["a field whose type changed"] = (
            "public static class H { public static int K; } public static class C { public static object F() => H.K; }",
            "public static class H { public static long K; } public static class C { public static object F() => H.K; }",
            false),
        ["another field"] = (
            "public static class H { public static int A; public static int B; } public static class C { public static int F() => H.A; }",
            "public static class H { public static int A; public static int B; } public static class C { public static int F() => H.B; }",
            false),
        ["an instance field and a framework field"] = (
            "public class B { public int X; } public static class C { public static string F(B b) => b.X == 0 ? string.Empty : null; }",
            "public class B { public int X; } public static class C { public static string F(B b) => b.X == 0 ? string.Empty : null; }",
            true),
        ["a field with initial data"] = (
            "public static class C { public static byte F() { System.ReadOnlySpan<byte> d = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; return d[0]; } }",
            "public static class C { public static byte F() { System.ReadOnlySpan<byte> d = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; return d[0]; } }",
            false),
        ["another cast"] = (
            "public static class C { public static object F(object o) => (string)o; }",
            "public static class C { public static object F(object o) => (System.Uri)o; }",
            false),
        ["type parameters, arrays, generics and references"] = (
            "public static class Box<T> { public static System.Type Get() => typeof(T); } public static class C { public static System.Type F<T>(ref int x) { x = 1; return typeof(T) ?? typeof(int[]) ?? typeof(System.Collections.Generic.List<int>) ?? Box<int>.Get(); } }",
            "public static class Box<T> { public static System.Type Get() => typeof(T); } public static class C { public static System.Type F<T>(ref int x) { x = 1; return typeof(T) ?? typeof(int[]) ?? typeof(System.Collections.Generic.List<int>) ?? Box<int>.Get(); } }",
            true),
        ["a pointer type"] = (
            "public static class C { public static unsafe System.Type F() => typeof(int*); }",
            "public static class C { public static unsafe System.Type F() => typeof(int*); }",
            false),
        ["a value type of the library"] = (
            "public struct S { } public static class C { public static System.Type F() => typeof(S); }",
            "public struct S { } public static class C { public static System.Type F() => typeof(S); }",
            false),
        ["a type of the library whose flags changed"] = (
            "public class D { } public static class C { public static System.Type F() => typeof(D) ?? typeof(D); }",
            "public sealed class D { } public static class C { public static System.Type F() => typeof(D) ?? typeof(D); }",
            false),
        ["a type of the library named twice"] = (
            "public class D { } public static class C { public static System.Type F() => typeof(D) ?? typeof(D); }",
            "public class D { } public static class C { public static System.Type F() => typeof(D) ?? typeof(D); }",
            true),
        ["a call through a function pointer"] = (
            "public static class C { public static unsafe int F() { delegate*<int> f = &G; return f(); } static int G() => 1; }",
            "public static class C { public static unsafe int F() { delegate*<int> f = &G; return f(); } static int G() => 1; }",
            false),
        ["an extern member"] = (
            "public static class C { public static int F() => G(); [System.Runtime.InteropServices.DllImport(\"x\")] static extern int G(); }",
            "public static class C { public static int F() => G(); [System.Runtime.InteropServices.DllImport(\"x\")] static extern int G(); }",
            false),
        ["a member that became extern"] = (
            "public static class C { public static int F() => G(); static int G() => 1; }",
            "public static class C { public static int F() => G(); [System.Runtime.InteropServices.DllImport(\"x\")] static extern int G(); }",
            false),
        ["a member of a value type"] = (
            "public struct S { public static int G() => 1; } public static class C { public static int F() => S.G(); }",
            "public struct S { public static int G() => 1; } public static class C { public static int F() => S.G(); }",
            false),
        ["a member of a class that became a value type"] = (
            "public class S { public static int G() => 1; } public static class C { public static int F() => S.G(); }",
            "public struct S { public static int G() => 1; } public static class C { public static int F() => S.G(); }",
            false),
        ["as many member pairs as the bound"] = (Chain(TwoVersions.Bound - 2), Chain(TwoVersions.Bound - 2), true),
        ["one member pair more than the bound"] = (Chain(TwoVersions.Bound - 1), Chain(TwoVersions.Bound - 1), false),
        ["a type parameter of the method for one of its type"] = (
            "public static class Box<T> { public static System.Type Get<U>() => typeof(T); } public static class C { public static System.Type F() => Box<int>.Get<int>(); }",
            "public static class Box<T> { public static System.Type Get<U>() => typeof(U); } public static class C { public static System.Type F() => Box<int>.Get<int>(); }",
            false),
        ["another array rank"] = (
            "public static class C { public static System.Type F() => typeof(int[]); }",
            "public static class C { public static System.Type F() => typeof(int[,]); }",
            false),
        ["a parameter by reference"] = ("public static class C { public static int F(ref int x) => 1; }", "public static class C { public static int F(int x) => 1; }", false),
        ["locals not initialised"] = (
            "public static class C { public static object F(string s) { object o = s; return o; } }",
            "public static class C { [System.Runtime.CompilerServices.SkipLocalsInit] public static object F(string s) { object o = s; return o; } }",
            false),
        ["a framework interface member a version implements explicitly"] = (
            "public static class C { public static void F(System.IDisposable d) => d.Dispose(); }",
            "public static class C { public static void F(System.IDisposable d) => d.Dispose(); } public class D : System.IDisposable { void System.IDisposable.Dispose() { } }",
            false),
        ["a virtual callee"] = ("public class C { public virtual int F() => 1; }", "public class C { public virtual int F() => 1; }", false),
        ["a callee that became virtual"] = ("public class C { public int F() => 1; }", "public class C { public virtual int F() => 1; }", false),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static TheoryData<string> CaseNames => [.. Cases.Keys.Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void DecidesEachCase(string name)
    {
        (string legacy, string modern, bool same) = Cases[name];

        Assert.Equal(same, Compare(Library("Lib", legacy), Library("Lib", modern)));
    }

    /// <summary>
    /// The case the sample shows: an extension class renamed between the versions, so the same text binds to two members, in
    /// a nested type on one side, with the same body. One call. A member whose body changed is not.
    /// </summary>
    [Fact]
    public void AMemberMovedToAnotherTypeWithItsBodyIsOneCall()
    {
        Compilation legacy = App(Library("Lib", "public static class TextExtensions { public static string Shout(this string s) => s.ToUpperInvariant() + \"!\"; public static string Whisper(this string s) => s.ToLowerInvariant(); }"));
        Compilation modern = App(Library("Lib", "public static class StringExtensions { public static string Shout(this string s) => s.ToUpperInvariant() + \"!\"; public static string Whisper(this string s) => s.ToLowerInvariant() + \".\"; }"));
        TwoVersions versions = new();

        Assert.True(versions.Same(Method(legacy, "Lib.TextExtensions", "Shout"), legacy, Method(modern, "Lib.StringExtensions", "Shout"), modern));
        Assert.True(versions.Same(Method(legacy, "Lib.TextExtensions", "Shout"), legacy, Method(modern, "Lib.StringExtensions", "Shout"), modern));
        Assert.False(versions.Same(Method(legacy, "Lib.TextExtensions", "Whisper"), legacy, Method(modern, "Lib.StringExtensions", "Whisper"), modern));
    }

    /// <summary>A member of a nested type of a generic type: the outer type's type arguments are the call's too.</summary>
    [Fact]
    public void AMemberOfANestedTypeIsReadWithItsOuterTypes()
    {
        const string Source = "public static class Outer<T> { public static class Inner { public static int F(int a) => a + 1; } }";
        Compilation legacy = App(Library("Lib", Source));
        Compilation modern = App(Library("Lib", Source));
        static IMethodSymbol Inner(Compilation compilation) =>
            compilation.GetTypeByMetadataName("Lib.Outer`1")!.Construct(compilation.GetSpecialType(SpecialType.System_Int32)).GetTypeMembers("Inner").Single().GetMembers("F").OfType<IMethodSymbol>().Single();

        Assert.True(new TwoVersions().Same(Inner(legacy), legacy, Inner(modern), modern));
    }

    /// <summary>An extension method on one side and an instance method on the other: the instance call checks its receiver first.</summary>
    [Fact]
    public void AStaticMemberAndAnInstanceMemberAreNotOneCall()
    {
        Compilation legacy = App(Library("Lib", "public class D { } public static class C { public static int F(this D d) => 1; }"));
        Compilation modern = App(Library("Lib", "public class D { public int F() => 1; }"));

        Assert.False(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.D", "F"), modern));
    }

    [Theory]
    [InlineData("int", "int", true)]
    [InlineData("int", "long", false)]
    public void TheTypeArgumentsAtTheCallAreTheSame(string legacyArgument, string modernArgument, bool same)
    {
        const string Source = "public static class C { public static T F<T>(T x) => x; }";
        Compilation legacy = App(Library("Lib", Source));
        Compilation modern = App(Library("Lib", Source));

        Assert.Equal(same, new TwoVersions().Same(Constructed(legacy, legacyArgument), legacy, Constructed(modern, modernArgument), modern));
    }

    [Fact]
    public void MembersOfTwoAssembliesAreNotOneCall() =>
        Assert.False(Compare(Library("LibA", $"namespace Lib {{ {Same} }}", wrap: false), Library("LibB", $"namespace Lib {{ {Same} }}", wrap: false)));

    [Fact]
    public void AnUnknownCalleeIsNotOneCall()
    {
        Compilation app = App(Library("Lib", Same));

        Assert.False(new TwoVersions().Same(legacy: null, app, Method(app, "Lib.C", "F"), app));
        Assert.False(new TwoVersions().Same(Method(app, "Lib.C", "F"), app, modern: null, app));
    }

    /// <summary>A reference assembly's bodies are stubs: on either side, the pair is never read.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AReferenceAssemblyIsNeverRead(bool legacyOnly, bool modernOnly) =>
        Assert.False(Compare(Library("Lib", Same, metadataOnly: legacyOnly), Library("Lib", Same, metadataOnly: modernOnly)));

    /// <summary>A project of the solution is read from its compilation, emitted; one that does not emit is never read.</summary>
    [Fact]
    public void AProjectReferenceIsReadFromItsCompilation()
    {
        Compilation library = RoslynTestCompilations.Compile($"namespace Lib {{ {Same} }}", "Lib");
        Compilation broken = RoslynTestCompilations.Compile($"namespace Lib {{ {Same} public class X {{ Missing m; }} }}", "Lib");
        Compilation legacy = RoslynTestCompilations.Compile("class App { }", [library.ToReference()], "App");
        Compilation modern = RoslynTestCompilations.Compile("class App { }", [library.ToReference()], "App");
        Compilation failing = RoslynTestCompilations.Compile("class App { }", [broken.ToReference()], "App");

        Assert.True(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.C", "F"), modern));
        Assert.False(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(failing, "Lib.C", "F"), failing));
    }

    /// <summary>A member of the calling project itself is read from that project's compilation, unless it does not emit.</summary>
    [Fact]
    public void AMemberOfTheCallingProjectIsReadFromIt()
    {
        Compilation legacy = RoslynTestCompilations.Compile($"namespace Lib {{ {Same} }}", "Lib");
        Compilation modern = RoslynTestCompilations.Compile($"namespace Lib {{ {Same} }}", "Lib");
        Compilation broken = RoslynTestCompilations.Compile($"namespace Lib {{ {Same} public class X {{ Missing m; }} }}", "Lib");

        Assert.True(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.C", "F"), modern));
        Assert.False(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(broken, "Lib.C", "F"), broken));
    }

    /// <summary>
    /// A file reference with no path, one whose file has gone, and a symbol the compilation does not reference, have no
    /// module to read.
    /// </summary>
    [Fact]
    public void AReferenceWithNoFileToReadIsNeverRead()
    {
        string path = Library("Lib", Same);
        Compilation pathless = RoslynTestCompilations.Compile("class App { }", [MetadataReference.CreateFromImage(File.ReadAllBytes(path))], "App");
        string removed = Library("Lib", Same);
        Compilation gone = App(removed);
        File.Delete(removed);
        Compilation present = App(path);
        Compilation other = RoslynTestCompilations.Compile("class App { }", "App");

        Assert.False(new TwoVersions().Same(Method(present, "Lib.C", "F"), present, Method(pathless, "Lib.C", "F"), pathless));
        Assert.False(new TwoVersions().Same(Method(present, "Lib.C", "F"), present, Method(gone, "Lib.C", "F"), gone));
        Assert.False(new TwoVersions().Same(Method(present, "Lib.C", "F"), present, Method(present, "Lib.C", "F"), other));
    }

    /// <summary>
    /// Rule 5 for a member of another assembly: the same build on both sides, or a framework (reference) assembly on both,
    /// whatever its build; another build of an ordinary assembly, or one that did not resolve, is not the same.
    /// </summary>
    [Theory]
    [InlineData("same build", true)]
    [InlineData("another build", false)]
    [InlineData("framework builds", true)]
    [InlineData("unresolved", false)]
    public void AMemberOfAnotherAssemblyIsTheSameOnlyFromTheSameBuildOrTheFramework(string dependency, bool same)
    {
        const string Dep = "namespace Dep { public static class D { public static int G() => 1; } }";
        const string Other = "namespace Dep { public static class D { public static int G() => 2; } }";
        const string Calls = "public static class C { public static int F() => Dep.D.G(); }";
        string first = Library("Dep", Dep, metadataOnly: string.Equals(dependency, "framework builds", StringComparison.Ordinal), wrap: false);
        string second = dependency switch
        {
            "same build" or "unresolved" => first,
            "another build" => Library("Dep", Other, wrap: false),
            _ => Library("Dep", Other, metadataOnly: true, wrap: false),
        };
        Compilation legacy = App(Library("Lib", Calls, references: [Reference(first)]), Reference(first));
        Compilation modern = string.Equals(dependency, "unresolved", StringComparison.Ordinal)
            ? App(Library("Lib", Calls, references: [Reference(second)]))
            : App(Library("Lib", Calls, references: [Reference(second)]), Reference(second));

        Assert.Equal(same, new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.C", "F"), modern));
    }

    /// <summary>A type or method that moved out of the library into another assembly under the same name is not the same.</summary>
    [Theory]
    [InlineData("public static class C { public static int F() => Lib.U.G(); }")]
    [InlineData("public static class C { public static System.Type F() => typeof(Lib.U); }")]
    public void AMemberThatMovedToAnotherAssemblyIsNotTheSame(string calls)
    {
        const string U = "public static class U { public static int G() => 1; }";
        string dep = Library("Dep", $"namespace Lib {{ {U} }}", wrap: false);
        Compilation legacy = App(Library("Lib", U + calls));
        Compilation modern = App(Library("Lib", calls, references: [Reference(dep)]), Reference(dep));

        Assert.False(new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.C", "F"), modern));
    }

    private static string Chain(int length) =>
        "public static class C { public static int F() => M0(); "
        + string.Concat(Enumerable.Range(0, length).Select(static i => string.Create(CultureInfo.InvariantCulture, $"static int M{i}() => M{i + 1}(); ")))
        + string.Create(CultureInfo.InvariantCulture, $"static int M{length}() => 0; }}");

    private static bool Compare(string legacyLibrary, string modernLibrary)
    {
        Compilation legacy = App(legacyLibrary);
        Compilation modern = App(modernLibrary);
        return new TwoVersions().Same(Method(legacy, "Lib.C", "F"), legacy, Method(modern, "Lib.C", "F"), modern);
    }

    /// <summary>A library built to a file of its own, its source in namespace <paramref name="assembly"/> unless <paramref name="wrap"/> is false.</summary>
    private static string Library(string assembly, string source, bool metadataOnly = false, bool wrap = true, MetadataReference[]? references = null) =>
        RoslynTestCompilations.EmitLibrary(assembly, wrap ? $"namespace {assembly} {{ {source} }}" : source, metadataOnly, references);

    private static PortableExecutableReference Reference(string path) => RoslynTestCompilations.FileReference(path);

    private static Compilation App(string library, params MetadataReference[] references) =>
        RoslynTestCompilations.Compile("class App { }", [Reference(library), .. references], "App");

    private static IMethodSymbol Method(Compilation compilation, string type, string name) =>
        compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single();

    private static IMethodSymbol Constructed(Compilation compilation, string argument) =>
        Method(compilation, "Lib.C", "F").Construct(compilation.GetSpecialType(string.Equals(argument, "int", StringComparison.Ordinal) ? SpecialType.System_Int32 : SpecialType.System_Int64));
}
