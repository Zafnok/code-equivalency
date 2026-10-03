using System.Collections.Immutable;

using Equiv.Core.Configuration;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// ADR 0047 (ticket P2-068): which methods are forwarders, and what a call to one is a call to. Each case is one static
/// method <c>F</c> of a class <c>C</c>; the targets are in <c>T</c>.
/// </summary>
public sealed class ForwardersTests
{
    private const string Targets = """
        public class T
        {
            public static bool G(string s) => s is null;
            public static void V(string s) { }
            public static int Two(string s, int n) => n;
            public static int Optional(string s, int n = 1) => n;
            public static int Many(params int[] xs) => xs.Length;
            public static int ByRef(ref int x) => x;
            public static int ByIn(in int x) => x;
            public static int Val(int x) => x;
            public static ref int First(int[] xs) => ref xs[0];
            public static int Wide(long x) => 0;
            public static dynamic Dyn(dynamic d) => d;
            public static object Obj(object o) => o;
            public static System.Threading.Tasks.Task Later(string s) => System.Threading.Tasks.Task.CompletedTask;
            public static T2 Generic<T2>(T2 x) => x;
            public bool Instance(string s) => s is null;
        }
        """;

    /// <summary>The shapes ADR 0047 names: an arrow body, a block that returns the call, and a call that returns nothing.</summary>
    [Theory]
    [InlineData("static bool F(string s) => T.G(s);", "T.G(string)")]
    [InlineData("static bool F(string s) { return T.G(s); }", "T.G(string)")]
    [InlineData("static void F(string s) => T.V(s);", "T.V(string)")]
    [InlineData("static void F(string s) { T.V(s); }", "T.V(string)")]
    [InlineData("static int F(string s, int n) => T.Two(s, n);", "T.Two(string, int)")]
    [InlineData("static int F(string s, int n) => T.Two(n: n, s: s);", "T.Two(string, int)")]
    [InlineData("static int F(params int[] xs) => T.Many(xs);", "T.Many(params int[])")]
    [InlineData("static int F(int x) => T.Generic<int>(x);", "T.Generic<int>(int)")]
    [InlineData("static bool F(this string s) => T.G(s);", "T.G(string)")]
    [InlineData("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)] static bool F(string s) => T.G(s);", "T.G(string)")]
    public void AStaticMethodWhoseBodyIsOneCallOfItsOwnParametersIsAForwarder(string forwarder, string target) =>
        Assert.Equal(target, Resolve(forwarder));

    /// <summary>Every condition of ADR 0047, one at a time: a method that fails one is an ordinary callee.</summary>
    [Theory]
    [InlineData("static bool F(string s) => T.G(s.Trim());")] // changes the argument
    [InlineData("static bool F(string s) => T.G(null);")] // passes something else
    [InlineData("static int F(string s, int n) => T.Two(s, 2);")] // one parameter is not passed
    [InlineData("static int F(string s) => T.Optional(s);")] // the target takes a default the forwarder does not
    [InlineData("static int F(int n, string s) => T.Two(s, n);")] // reordered
    [InlineData("static int F(int a, int b) => T.Many(a, b);")] // packed into a params array
    [InlineData("static int F(int x) => T.Wide(x);")] // converted
    [InlineData("static long F(string s, int n) => T.Two(s, n);")] // the result is converted
    [InlineData("static int F(int x) => T.ByRef(ref x);")] // the target writes through its parameter
    [InlineData("static int F(int x) => T.ByIn(x);")] // the target takes a reference
    [InlineData("static int F(ref int x) => T.ByRef(ref x);")]
    [InlineData("static int F(in int x) => T.ByIn(x);")]
    [InlineData("static int F(ref int x) => T.Val(x);")] // the forwarder takes a reference
    [InlineData("static ref int F(int[] xs) => ref T.First(xs);")] // returns a reference
    [InlineData("static bool F(string s) { throw new System.Exception(); }")] // one statement that is no call
    [InlineData("static object F(object o) => T.Dyn(o);")] // the same call only up to an identity conversion
    [InlineData("static dynamic F(dynamic d) => T.Obj(d);")]
    [InlineData("static bool F(string s) { System.Console.WriteLine(); return T.G(s); }")] // a second statement
    [InlineData("static bool F(string s) { if (s is null) { return true; } return T.G(s); }")]
    [InlineData("static bool F(string s) => !T.G(s);")] // not a call
    [InlineData("static bool F(string s) => s is null;")]
    [InlineData("static void F(string s) { }")]
    [InlineData("static void F(string s) { return; }")]
    [InlineData("static async System.Threading.Tasks.Task F(string s) => T.Later(s);")] // the call's exception goes into the task
    [InlineData("static bool F(string s) => new T().Instance(s);")] // an instance target
    [InlineData("static T2 F<T2>(T2 x) => T.Generic<T2>(x);")] // a generic forwarder
    [InlineData("static extern bool F(string s);")] // no body
    [InlineData("[System.Diagnostics.Conditional(\"DEBUG\")] static void F(string s) => T.V(s);")] // may not be compiled
    [InlineData("[System.Security.SecurityCritical] static bool F(string s) => T.G(s);")] // may throw first
    [InlineData("static bool F(string s) => F(s);")] // forwards to itself
    [InlineData("static bool F(string s) => F2(s); static bool F2(string s) => F(s);")] // a cycle
    [InlineData("static bool F(string s) => F2(s); static bool F2(string s) => F3(s); static bool F3(string s) => F2(s);")] // into a cycle
    public void AnythingElseIsNotAForwarder(string forwarder) => Assert.Null(Resolve(forwarder));

    /// <summary>A static constructor runs before the forwarder, so the call does more than its target; a constant runs nothing.</summary>
    [Theory]
    [InlineData("static C() { }", false)]
    [InlineData("static readonly int Field = 1;", false)]
    [InlineData("static int Property { get; } = 1;", false)]
    [InlineData("const int Constant = 1;", true)]
    public void ATypeWithAStaticConstructorHasNoForwarders(string member, bool forwarder) =>
        Assert.Equal(forwarder ? "T.G(string)" : null, Resolve($"{member} static bool F(string s) => T.G(s);"));

    /// <summary>
    /// A generic type's method is constructed per call site, a security attribute on the type covers its members, and an
    /// instance method is called on a receiver.
    /// </summary>
    [Theory]
    [InlineData("class C { public bool F(string s) => T.G(s); }", "C")]
    [InlineData("static class C<TC> { public static bool F(string s) => T.G(s); }", "C`1")]
    [InlineData("static class Outer<TC> { public static class C { public static bool F(string s) => T.G(s); } }", "Outer`1+C")]
    [InlineData("[System.Security.SecuritySafeCritical] static class C { public static bool F(string s) => T.G(s); }", "C")]
    public void TheDeclaringTypeCanRuleAForwarderOut(string type, string name)
    {
        Compilation compilation = Compile(type + Targets);

        Assert.Null(Forwarders.Resolve(compilation.GetTypeByMetadataName(name)!.GetMembers("F").OfType<IMethodSymbol>().Single(), compilation));
    }

    /// <summary>An interface's static method with a body is a forwarder; a <c>static virtual</c> one is whatever its implementer says.</summary>
    [Theory]
    [InlineData("static bool F(string s) => T.G(s);", true)]
    [InlineData("static virtual bool F(string s) => T.G(s);", false)]
    public void AStaticVirtualInterfaceMemberIsNotAForwarder(string member, bool forwarder)
    {
        Compilation compilation = Compile($"interface C {{ {member} }}" + Targets);

        Assert.Equal(forwarder, Forwarders.Resolve(F(compilation), compilation) is not null);
    }

    /// <summary>A chain of forwarders is followed to its end, whichever link a call names.</summary>
    [Theory]
    [InlineData("F", "T.G(string)")]
    [InlineData("F2", "T.G(string)")]
    [InlineData("F3", "T.G(string)")]
    public void AChainIsFollowedToItsEnd(string from, string target)
    {
        Compilation compilation = Compile(Class("static bool F(string s) => F2(s); static bool F2(string s) => F3(s); static bool F3(string s) => T.G(s);"));

        Assert.Equal(target, Display(Forwarders.Resolve(compilation.GetTypeByMetadataName("C")!.GetMembers(from).OfType<IMethodSymbol>().Single(), compilation)));
    }

    /// <summary>
    /// A forwarder in another project of the solution is read from that project's source, and its target belongs to that
    /// project's compilation; the chain goes on through a third project. One that is only a compiled reference has no body.
    /// </summary>
    [Fact]
    public void AForwarderInAnotherProjectIsReadFromItsSource()
    {
        Compilation ends = RoslynTestCompilations.Compile("public static class End { public static bool Is(string s) => s is null; }", "Ends");
        Compilation helpers = RoslynTestCompilations.Compile(
            "public static class Text { public static bool Blank(string s) => Mid.Is(s); } public static class Mid { public static bool Is(string s) => End.Is(s); }",
            [ends.ToMetadataReference()],
            "Helpers");
        Compilation source = Calling(helpers.ToMetadataReference());
        Compilation compiled = Calling(Emitted(helpers), Emitted(ends));

        Forwarders.Resolved? resolved = Forwarders.Resolve(Callee(source), source);

        Assert.Equal("End.Is(string)", Display(resolved));
        Assert.Same(helpers, resolved!.Compilation);
        Assert.Null(Forwarders.Resolve(Callee(compiled), compiled));

        static Compilation Calling(params MetadataReference[] references) =>
            RoslynTestCompilations.Compile("class App { static bool M(string s) => Text.Blank(s); }", references, "App");

        static IMethodSymbol Callee(Compilation app) => app.GetTypeByMetadataName("Text")!.GetMembers("Blank").OfType<IMethodSymbol>().Single();
    }

    /// <summary>A method whose source is in no compilation the caller's knows has no body to read.</summary>
    [Fact]
    public void AMethodFromAnUnrelatedCompilationIsNotResolved()
    {
        Compilation compilation = Compile(Class("static bool F(string s) => T.G(s);"));

        Assert.Null(Forwarders.Resolve(F(compilation), RoslynTestCompilations.Compile("class Other { }", "Other")));
    }

    /// <summary>
    /// The two sides of a matched method agree when a call to it is the same call on both: neither is a forwarder, or both
    /// forward to one identity, which the rename map can make of two names.
    /// </summary>
    [Theory]
    [InlineData("static bool F(string s) => s is null;", "static bool F(string s) => s == null;", true)]
    [InlineData("static bool F(string s) => T.G(s);", "static bool F(string s) { return T.G(s); }", true)]
    [InlineData("static bool F(string s) => T.G(s);", "static bool F(string s) => s is null;", false)]
    [InlineData("static bool F(string s) => s is null;", "static bool F(string s) => T.G(s);", false)]
    [InlineData("static bool F(string s) => T.G(s);", "static bool F(string s) => Other(s); static bool Other(string s) => s is null;", false)]
    public void TheSidesOfAMatchedMethodAgreeWhenACallToItIsTheSameCall(string legacy, string modern, bool agree)
    {
        Compilation old = Compile(Class(legacy));
        Compilation @new = Compile(Class(modern));

        Assert.Equal(agree, Forwarders.Agree((F(old), old), (F(@new), @new), RenameMap.Empty));
    }

    /// <summary>A target the rename map renames is the same target.</summary>
    [Fact]
    public void ARenamedTargetIsTheSameTarget()
    {
        Compilation old = Compile("namespace Old { public static class T { public static bool G(string s) => s is null; } } static class C { static bool F(string s) => Old.T.G(s); }");
        Compilation @new = Compile("namespace New { public static class T { public static bool G(string s) => s is null; } } static class C { static bool F(string s) => New.T.G(s); }");
        RenameMap renames = new(ImmutableDictionary<string, string>.Empty.Add("Old", "New"), []);

        Assert.True(Forwarders.Agree((F(old), old), (F(@new), @new), renames));
        Assert.False(Forwarders.Agree((F(old), old), (F(@new), @new), RenameMap.Empty));
    }

    /// <summary>The entry point of top-level statements is an ordinary static method whose declaration is the compilation unit.</summary>
    [Fact]
    public void TheEntryPointOfTopLevelStatementsIsNotAForwarder()
    {
        Compilation compilation = RoslynTestCompilations.Compile("T.V(null);" + Targets);
        SyntaxTree tree = compilation.SyntaxTrees.Single();
        IMethodSymbol main = Assert.IsType<IMethodSymbol>(compilation.GetSemanticModel(tree).GetDeclaredSymbol(tree.GetRoot(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken), exactMatch: false);

        Assert.Equal(MethodKind.Ordinary, main.MethodKind);
        Assert.Null(Forwarders.Resolve(main, compilation));
    }

    private static string? Resolve(string members)
    {
        Compilation compilation = Compile(Class(members));
        return Display(Forwarders.Resolve(F(compilation), compilation));
    }

    private static string Class(string members) => $"static partial class C {{ {members} }}" + Targets;

    private static IMethodSymbol F(Compilation compilation) => compilation.GetTypeByMetadataName("C")!.GetMembers("F").OfType<IMethodSymbol>().Single();

    private static string? Display(Forwarders.Resolved? resolved) => resolved?.Target.ToDisplayString();

    private static Compilation Compile(string source)
    {
        Compilation compilation = RoslynTestCompilations.Compile(source);
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return compilation;
    }

    private static PortableExecutableReference Emitted(Compilation compilation)
    {
        using MemoryStream image = new();
        Assert.True(compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken).Success);
        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
