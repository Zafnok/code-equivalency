using System.Linq;

using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Lowering;
using Equiv.TestSupport;

using Microsoft.CodeAnalysis;

using Xunit;

using static Equiv.Frontend.CSharp.Tests.Lowering.Lowered;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-074: a path-validation row does not flag a call whose path the method shows to be valid. Each member's
/// path calls (<c>new StreamReader(path)</c>, <c>Path.Combine</c>, <c>Path.GetDirectoryName</c>) have such a row, and
/// nothing else the member calls has a row at all.
/// </summary>
public sealed class ValidPathsTests
{
    /// <summary>The result of a listed source is a valid path; anything else that is not a constant is not.</summary>
    [Theory]
    [InlineData("static object M() => new StreamReader(typeof(C).Assembly.Location);", false)]
    [InlineData("static object M() => new StreamReader(Path.GetTempFileName());", false)]
    [InlineData("static object M() => Path.GetDirectoryName(typeof(C).Assembly.Location);", false)]
    [InlineData("static object M(string p) => new StreamReader(p);", true)]
    [InlineData("static object M(string p) => Path.Combine(p, \"Images\");", true)]
    [InlineData("static object M() => new StreamReader(Environment.MachineName);", true)]
    [InlineData("static object M() => new StreamReader(Guid.NewGuid().ToString());", true)]
    [InlineData("static string F;\nstatic object M() => new StreamReader(F);", true)]
    public void ASourceYieldsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>A path member called with valid paths and plain constants returns a valid path.</summary>
    [Theory]
    [InlineData("static object M() => new StreamReader(Path.Combine(AppContext.BaseDirectory, \"a.txt\"));", false)]
    [InlineData("static object M() => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(AppContext.BaseDirectory)), \"Images\");", false)]
    [InlineData("static object M() => new StreamReader(Path.Combine(AppContext.BaseDirectory, \"a|b\"));", true)]
    [InlineData("static object M(string p) => new StreamReader(Path.Combine(AppContext.BaseDirectory, p));", true)]
    [InlineData("static object M() => new StreamReader(Path.Combine(new[] { AppContext.BaseDirectory, \"a.txt\" }));", true)]
    public void ATransformOfValidPathsYieldsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>A local is a valid path when its initializer and every assignment to it is one, and none is unknown.</summary>
    [Theory]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; return new StreamReader(d); }", false)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d = Path.GetTempPath(); return new StreamReader(d); }", false)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; var e = Path.Combine(d, \"x\"); return new StreamReader(e); }", false)]
    [InlineData("static void Use(string s) { }\nstatic object M() { var d = AppContext.BaseDirectory; Use(d); return new StreamReader(d); }", false)]
    [InlineData("static object M(string p) { var d = AppContext.BaseDirectory; d = p; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { var x = AppContext.BaseDirectory; var d = p; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { var d = p; var x = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += \"|\"; return new StreamReader(d); }", true)]
    [InlineData("static void Fill(ref string s) { }\nstatic object M() { var d = AppContext.BaseDirectory; Fill(ref d); return new StreamReader(d); }", true)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; (d, _) = (\"a|b\", 1); return new StreamReader(d); }", true)]
    [InlineData("static object M() { string d; d = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static object M() { var a = AppContext.BaseDirectory; var b = a; a = b; return new StreamReader(a); }", true)]
    [InlineData("static object M(string[] fs) { foreach (var f in fs) { return new StreamReader(f); } return null; }", true)]
    [InlineData("static object M(object o) { if (o is string s) { return new StreamReader(s); } return null; }", true)]
    public void ALocalThatOnlyHoldsValidPathsIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>A parameter or local nothing assigns is a valid path where a listed guard has returned true for it.</summary>
    [Theory]
    [InlineData("static object M(string p) => File.Exists(p) ? new StreamReader(p) : null;", false)]
    [InlineData("static object M(string p) => !File.Exists(p) ? null : new StreamReader(p);", false)]
    [InlineData("static object M(string p) => Directory.Exists(p) ? Path.Combine(p, \"a\") : null;", false)]
    [InlineData("static object M(string p) { if (File.Exists(p)) { return new StreamReader(p); } return null; }", false)]
    [InlineData("static object M(string a) { var p = a + \".txt\"; return File.Exists(p) ? new StreamReader(p) : null; }", false)]
    [InlineData("static object M(string p, bool b) => b && File.Exists(p) ? new StreamReader(p) : null;", false)]
    [InlineData("static object M(string p, bool b) => File.Exists(p) && b ? new StreamReader(p) : null;", false)]
    [InlineData("static object M(string p) => File.Exists(p) ? null : new StreamReader(p);", true)]
    [InlineData("static object M(string p) => !File.Exists(p) ? new StreamReader(p) : null;", true)]
    [InlineData("static object M(string p, string q) => File.Exists(q) ? new StreamReader(p) : null;", true)]
    [InlineData("static object M(string p, bool b) => b || File.Exists(p) ? new StreamReader(p) : null;", true)]
    [InlineData("static object M(string p) => p.Length > 3 ? new StreamReader(p) : null;", true)]
    [InlineData("static object M(string p) => !string.IsNullOrEmpty(p) ? new StreamReader(p) : null;", true)]
    [InlineData("static object M(string p) => string.IsNullOrEmpty(p) ? null : new StreamReader(p);", true)]
    [InlineData("static object M(string p) => (new StreamReader(p).EndOfStream && File.Exists(p)) ? null : p;", true)]
    [InlineData("static object M(string p, string q) { if (File.Exists(p)) { p = q; return new StreamReader(p); } return null; }", true)]
    public void AGuardedUseIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>The right operand of <c>&amp;&amp;</c> runs when the left is true, and that of <c>||</c> when it is false.</summary>
    [Theory]
    [InlineData("static bool M(string p) => File.Exists(p) && new StreamReader(p).EndOfStream;", false)]
    [InlineData("static bool M(string p) => !File.Exists(p) || new StreamReader(p).EndOfStream;", false)]
    [InlineData("static bool M(string p) => File.Exists(p) || new StreamReader(p).EndOfStream;", true)]
    [InlineData("static bool M(string p) => !File.Exists(p) && new StreamReader(p).EndOfStream;", true)]
    [InlineData("static bool M(string p) => File.Exists(p) & new StreamReader(p).EndOfStream;", true)]
    [InlineData("static bool M(string p) => !File.Exists(p) | new StreamReader(p).EndOfStream;", true)]
    [InlineData("static bool M(string p) => (new StreamReader(p).EndOfStream && File.Exists(p)) && p.Length > 0;", true)]
    [InlineData("static bool M(string p) => (new StreamReader(p).EndOfStream || !File.Exists(p)) || p.Length > 0;", true)]
    public void AShortCircuitOperandIsGuardedByTheOneBeforeIt(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>An <c>if</c> without an <c>else</c> that returns or throws when the guard is false guards what follows it in the block.</summary>
    [Theory]
    [InlineData("static object M(string p) { if (!File.Exists(p)) return null; return new StreamReader(p); }", false)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) { throw new ArgumentException(p); } return new StreamReader(p); }", false)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) { GC.KeepAlive(p); return null; } GC.KeepAlive(p); return new StreamReader(p); }", false)]
    [InlineData("static object M(string p, bool b) { if (b || !File.Exists(p)) return null; return new StreamReader(p); }", false)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) return null; if (p.Length > 3) { return new StreamReader(p); } return null; }", false)]
    [InlineData("static object M(string p, bool b) { if (b && !File.Exists(p)) return null; return new StreamReader(p); }", true)]
    [InlineData("static object M(string p) { if (File.Exists(p)) return null; return new StreamReader(p); }", true)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) { GC.KeepAlive(p); } return new StreamReader(p); }", true)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) { } return new StreamReader(p); }", true)]
    [InlineData("static object M(string p) { if (!File.Exists(p)) return null; else GC.KeepAlive(p); return new StreamReader(p); }", true)]
    [InlineData("static object M(string p) { var r = new StreamReader(p); if (!File.Exists(p)) return null; return r; }", true)]
    [InlineData("static object M(string p, string q) { if (!File.Exists(p)) return null; p = q; return new StreamReader(p); }", true)]
    public void AnEarlyExitGuardsTheRestOfItsBlock(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>A field initializer is lowered into the constructor, and its site is read in the initializer's own tree.</summary>
    [Fact]
    public void AFieldInitializerIsReadWhereItIsDeclared()
    {
        const string Valid = "using System.IO;\nclass C\n{\n    string f = Path.GetDirectoryName(System.AppContext.BaseDirectory);\n    C() { }\n}\n";
        const string Unknown = "using System.IO;\nclass C\n{\n    static string P;\n    string f = Path.GetDirectoryName(P);\n    C() { }\n}\n";

        Assert.DoesNotContain(Calls(Source(Valid, ".ctor")), static call => call.Callee.RuntimeChanged);
        Assert.Contains(Calls(Source(Unknown, ".ctor")), static call => call.Callee.RuntimeChanged);
    }

    /// <summary>
    /// Ticket P2-158: a property that overrides only the setter has no getter of its own, so a read of it names no
    /// source and is not a valid path, as the operand itself or as what a local holds.
    /// </summary>
    [Theory]
    [InlineData("static object M(Leaf leaf) => new StreamReader(leaf.Text);")]
    [InlineData("static object M(Leaf leaf) { var p = leaf.Text; return new StreamReader(p); }")]
    public void AReadOfAPropertyThatOverridesOnlyTheSetterIsNotAValidPath(string member) => Assert.True(Flags(
        "abstract class Node { public virtual string Text { get => null; set { } } }\nsealed class Leaf : Node { public override string Text { set { } } }\n" + member));

    /// <summary>
    /// Ticket P2-188: a validator's result is a valid path whatever its argument, so only the validator's own row fires on
    /// a path from a caller; a transform's result is not, and both rows fire.
    /// </summary>
    [Theory]
    [InlineData("static object M(string p) => Directory.CreateDirectory(Path.GetFullPath(p));", new[] { GetFullPath })]
    [InlineData("static object M(string p) => new StreamReader(Path.Combine(Path.GetFullPath(p), \"a.txt\"));", new[] { GetFullPath })]
    [InlineData("static object M(string p) => Directory.CreateDirectory(Path.GetFileName(p));", new[] { CreateDirectory, "System.IO.Path::GetFileName(string)" })]
    public void AValidatorsResultIsAValidPathWhateverItsArgument(string member, string[] flagged) => Assert.Equal(flagged, Flagged(member));

    /// <summary>Ticket P2-188: the start of a valid path is one, and so is a conditional expression both of whose branches are.</summary>
    [Theory]
    [InlineData("static object M(string p) { var f = Path.GetFullPath(p); return Directory.CreateDirectory(f.Length > 1 ? f.Substring(0, f.Length - 1) : f); }", new[] { GetFullPath })]
    [InlineData("static object M(string p) { var f = Path.GetFullPath(p); return Directory.CreateDirectory(f.Substring(1, f.Length - 1)); }", new[] { CreateDirectory, GetFullPath })]
    [InlineData("static object M(string p) { var f = Path.GetFullPath(p); return Directory.CreateDirectory(f.Substring(0)); }", new[] { CreateDirectory, GetFullPath })]
    [InlineData("static object M(string p) { var f = Path.GetFullPath(p); return Directory.CreateDirectory(f.Length > 1 ? f : p); }", new[] { CreateDirectory, GetFullPath })]
    [InlineData("static object M(string p) { var f = Path.GetFullPath(p); return Directory.CreateDirectory(f.Length > 1 ? p : f); }", new[] { CreateDirectory, GetFullPath })]
    [InlineData("static object M(string p) => Directory.CreateDirectory(p.Substring(0, 2));", new[] { CreateDirectory })]
    public void TheStartOfAValidPathIsOne(string member, string[] flagged) => Assert.Equal(flagged, Flagged(member));

    /// <summary>
    /// Ticket P2-188: a call to one of the solution's own methods or properties that no override replaces yields a valid
    /// path when every value its body returns is one, read in that body alone. A path that really comes from the caller
    /// still fires the row.
    /// </summary>
    [Theory]
    [InlineData("static string Dir() => Path.Combine(Path.GetDirectoryName(typeof(C).Assembly.Location), \"Translation\");\nstatic object M() => new StreamReader(Path.Combine(Dir(), \"a.txt\"));", false)]
    [InlineData("static string Normalize(string p) { var f = Path.GetFullPath(p); return f.Length > 1 && f[f.Length - 1] == Path.DirectorySeparatorChar ? f.Substring(0, f.Length - 1) : f; }\nstatic object M(string p) => Directory.CreateDirectory(Normalize(p));", false)]
    [InlineData("static string Existing(string p) => File.Exists(p) ? p : AppContext.BaseDirectory;\nstatic object M(string p) => new StreamReader(Existing(p));", false)]
    [InlineData("static string Pick(bool b) { if (b) { return AppContext.BaseDirectory; } return Path.GetTempPath(); }\nstatic object M(bool b) => new StreamReader(Pick(b));", false)]
    [InlineData("static string Root { get { return AppContext.BaseDirectory; } }\nstatic object M() => new StreamReader(Root);", false)]
    [InlineData("static string Root => AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", false)]
    [InlineData("static string Dir() { Func<string> f = () => Environment.MachineName; GC.KeepAlive(f); return AppContext.BaseDirectory; }\nstatic object M() => new StreamReader(Dir());", false)]
    [InlineData("static string Dir() { return AppContext.BaseDirectory; string Other() => Environment.MachineName; }\nstatic object M() => new StreamReader(Dir());", false)]
    [InlineData("static object M() { var d = Dir(); return new StreamReader(d); }\nstatic string Dir() => AppContext.BaseDirectory;", false)]
    [InlineData("static string Same(string p) => p;\nstatic object M(string p) => new StreamReader(Same(p));", true)]
    [InlineData("static string Pick(bool b) { if (b) { return AppContext.BaseDirectory; } return Environment.MachineName; }\nstatic object M(bool b) => new StreamReader(Pick(b));", true)]
    [InlineData("static string Dir() { throw new InvalidOperationException(); }\nstatic object M() => new StreamReader(Dir());", true)]
    [InlineData("protected virtual string Dir() => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());", true)]
    [InlineData("static object M() { return new StreamReader(Dir()); string Dir() => AppContext.BaseDirectory; }", true)]
    [InlineData("static object M(Func<string> dir) => new StreamReader(dir());", true)]
    public void ASolutionMemberThatReturnsOnlyValidPathsYieldsOne(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-200: a returned call to another of the solution's members is followed into its body too, up to four calls
    /// deep. A path that really comes from the caller still fires the row however deep it is passed, and so does a value
    /// past the fourth call or a recursion that never reaches a valid path within four.
    /// </summary>
    [Theory]
    [InlineData("static string Inner() => AppContext.BaseDirectory;\nstatic string Outer() => Inner();\nstatic object M() => new StreamReader(Outer());", false)]
    [InlineData("static string Inner => AppContext.BaseDirectory;\nstatic string Outer => Inner;\nstatic object M() => new StreamReader(Outer);", false)]
    [InlineData("static string D1() => D2();\nstatic string D2() => D3();\nstatic string D3() => D4();\nstatic string D4() => AppContext.BaseDirectory;\nstatic object M() => new StreamReader(D1());", false)]
    [InlineData("static string D1() => D2();\nstatic string D2() => D3();\nstatic string D3() => D4();\nstatic string D4() => D5();\nstatic string D5() => AppContext.BaseDirectory;\nstatic object M() => new StreamReader(D1());", true)]
    [InlineData("static string Normalize(string p) { var f = Path.GetFullPath(p); return f.Length > 1 ? f.Substring(0, f.Length - 1) : f; }\nstatic string Snapshot(string p) => Normalize(\"/mnt\" + p);\nstatic object M(string p) => File.GetLastWriteTimeUtc(Snapshot(p));", false)]
    [InlineData("static string Inner(string p) => p;\nstatic string Outer(string p) => Inner(p);\nstatic object M(string p) => new StreamReader(Outer(p));", true)]
    [InlineData("static string Snapshot(string p) => Same(\"/mnt\" + p);\nstatic string Same(string p) => p;\nstatic object M(string p) => File.GetLastWriteTimeUtc(Snapshot(p));", true)]
    [InlineData("static string Dir(int n) => n > 0 ? Dir(n - 1) : AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Dir(2));", true)]
    public void AReturnedCallIsFollowedUpToFourCallsDeep(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>Ticket P2-188: a member an override can replace is not read, as an abstract or an overriding one shows.</summary>
    [Theory]
    [InlineData("interface I { string Dir(); }\nclass C\n{\nstatic object M(I i) => new StreamReader(i.Dir());\n}\n")]
    [InlineData("abstract class B { public abstract string Dir(); }\nclass C : B\n{\npublic override string Dir() => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());\n}\n")]
    [InlineData("interface I { static abstract string Dir(); }\nclass C\n{\nstatic object M<T>() where T : I => new StreamReader(T.Dir());\n}\n")]
    public void AMemberAnOverrideCanReplaceIsNotRead(string types) =>
        Assert.Contains(Calls(Source("using System;\nusing System.IO;\n" + types)), static call => call.Callee.RuntimeChanged);

    /// <summary>
    /// Ticket P2-200: on a receiver whose type is sealed, a virtual call runs the override that type declares, so that
    /// override's body is read, through more than one level of overrides; a sealed type that inherits its override, or
    /// declares only an overload, is not read; and an override that returns what is not a valid path fires the row
    /// whatever the member it overrides returns.
    /// </summary>
    [Theory]
    [InlineData("abstract class B { public abstract string Dir(); }\nsealed class C : B\n{\npublic override string Dir() => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());\n}\n", false)]
    [InlineData("abstract class B { public abstract string Root { get; } }\nsealed class C : B\n{\npublic override string Root => AppContext.BaseDirectory;\nobject M() => new StreamReader(Root);\n}\n", false)]
    [InlineData("abstract class A { public abstract string Dir(); }\nclass B : A { public override string Dir() => Environment.MachineName; }\nsealed class C : B\n{\npublic override string Dir() => AppContext.BaseDirectory;\nstatic object M(C c) => new StreamReader(c.Dir());\n}\n", false)]
    [InlineData("class B { public virtual string Dir() => AppContext.BaseDirectory; }\nsealed class C : B\n{\npublic override string Dir() => Environment.MachineName;\nobject M() => new StreamReader(Dir());\n}\n", true)]
    [InlineData("class B { public virtual string Dir() => AppContext.BaseDirectory; }\nsealed class C : B\n{\npublic string Dir(int n) => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());\n}\n", true)]
    public void AVirtualCallOnASealedTypeReadsItsOverride(string types, bool flagged) =>
        Assert.Equal(flagged, Calls(Source("using System;\nusing System.IO;\n" + types)).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>
    /// Ticket P2-200, Duplicati's <c>LinuxSnapshot</c> shape: a sealed snapshot type's override hands the path to a nested
    /// type's method, which returns it through a helper ending in <c>Path.GetFullPath</c>, three calls from the caller.
    /// The same chain whose helper returns its argument unchecked still fires the row.
    /// </summary>
    [Theory]
    [InlineData("Paths.Normalize", false)]
    [InlineData("Paths.Same", true)]
    public void ASnapshotPathThreeCallsDeepIsFollowed(string helper, bool flagged) => Assert.Equal(flagged, Calls(Source(
        "using System;\nusing System.IO;\n"
        + "abstract class SnapshotBase { public abstract string ToSnapshotPath(string localPath); }\n"
        + "sealed class C : SnapshotBase\n{\n"
        + "public override string ToSnapshotPath(string localPath) => Find(localPath).ToSnapshotPath(localPath);\n"
        + "private static Snapshot Find(string localPath) => new Snapshot();\n"
        + "object M(string localPath) => File.GetLastWriteTimeUtc(ToSnapshotPath(localPath));\n"
        + "private sealed class Snapshot { public string ToSnapshotPath(string localPath) => " + helper + "(\"/mnt\" + localPath); }\n}\n"
        + "static class Paths\n{\n"
        + "public static string Normalize(string p) { var f = Path.GetFullPath(p); return f.Length > 1 ? f.Substring(0, f.Length - 1) : f; }\n"
        + "public static string Same(string p) => p;\n}\n")).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>Ticket P2-188: a solution member in a project the caller's references as source is read with that project's compilation.</summary>
    [Fact]
    public void ASolutionMemberInAnotherProjectIsRead()
    {
        Compilation library = RoslynTestCompilations.Compile(
            "public static class Translator { public static string Dir() => System.IO.Path.Combine(System.AppContext.BaseDirectory, \"Translation\"); }", "Library");
        Compilation app = RoslynTestCompilations.Compile("class C { static object M() => new System.IO.StreamReader(Translator.Dir()); }", [library.ToMetadataReference()], "App");
        IMethodSymbol method = app.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        Assert.DoesNotContain(Calls(IrLowerer.Lower(method, app, RenameMap.Empty, [], Runtimes.Migration)), static call => call.Callee.RuntimeChanged);
    }

    /// <summary>
    /// Ticket P2-200: a callee's own returned call is read with the compilation that holds the callee, which can reference
    /// a project the caller's does not.
    /// </summary>
    [Fact]
    public void ACalleesCalleeInAProjectOnlyTheCalleeReferencesIsRead()
    {
        Compilation paths = RoslynTestCompilations.Compile(
            "public static class Paths { public static string Root() => System.AppContext.BaseDirectory; }", "Paths");
        Compilation library = RoslynTestCompilations.Compile(
            "public static class Translator { public static string Dir() => Paths.Root(); }", [paths.ToMetadataReference()], "Library");
        Compilation app = RoslynTestCompilations.Compile("class C { static object M() => new System.IO.StreamReader(Translator.Dir()); }", [library.ToMetadataReference()], "App");
        IMethodSymbol method = app.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        Assert.DoesNotContain(Calls(IrLowerer.Lower(method, app, RenameMap.Empty, [], Runtimes.Migration)), static call => call.Callee.RuntimeChanged);
    }

    /// <summary>
    /// Ticket P2-201: a <c>readonly</c> field of a class is a valid path when its initializer and every write in a constructor
    /// or <c>init</c> accessor of its type is one. A field that something else can write, or with a write whose value is not
    /// known or not valid, still fires the row.
    /// </summary>
    [Theory]
    [InlineData("static readonly string F = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(F);", false)]
    [InlineData("readonly string f = Path.Combine(AppContext.BaseDirectory, \"TAGMESSAGE\");\nobject M() => new StreamReader(Path.Combine(f, \"a.txt\"));", false)]
    [InlineData("readonly string f;\nC() { f = Path.GetTempPath(); }\nobject M() => new StreamReader(f);", false)]
    [InlineData("readonly string f;\nC(string p) { if (!Directory.Exists(p)) { throw new ArgumentException(p); } f = p; }\nobject M() => new StreamReader(f);", false)]
    [InlineData("static readonly string F;\nstatic C() { F = AppContext.BaseDirectory; }\nstatic object M() => new StreamReader(F);", false)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nC() { }\nC(int n) : this() { f = Path.GetTempPath(); }\nobject M() => new StreamReader(f);", false)]
    [InlineData("static readonly string A = AppContext.BaseDirectory;\nstatic readonly string B = Path.Combine(A, \"x\");\nstatic object M() => new StreamReader(B);", false)]
    [InlineData("static string F = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(F);", true)]
    [InlineData("readonly string f;\nC(string p) { f = p; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nC() { }\nC(string p) { f = p; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("static readonly string F;\nstatic object M() => new StreamReader(F);", true)]
    [InlineData("static readonly string F = Environment.MachineName;\nstatic object M() => new StreamReader(F);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nC() { f += \"|\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("static void Fill(ref string s) { }\nreadonly string f = AppContext.BaseDirectory;\nC() { Fill(ref f); }\nobject M() => new StreamReader(f);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nC() { ref string r = ref f; r = \"a|b\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nreadonly string g = AppContext.BaseDirectory;\nC(bool b) { (b ? ref f : ref g) = \"a|b\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\nC() { string s = \"\"; ref string r = ref s; r = ref f; r = \"a|b\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("readonly string f = AppContext.BaseDirectory;\npublic string Other { init { f = value; } }\nobject M() => new StreamReader(f);", true)]
    [InlineData("static readonly string A = B;\nstatic readonly string B = A;\nstatic object M() => new StreamReader(A);", true)]
    public void AReadonlyFieldThatOnlyHoldsValidPathsIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-201: a get-only auto-property is a valid path when its initializer and every assignment in a constructor of
    /// its type is one. A property with a setter, one whose getter has a body, and one an override can replace still fire.
    /// </summary>
    [Theory]
    [InlineData("static string Root { get; } = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", false)]
    [InlineData("string Root { get; }\nC() { Root = Path.GetTempPath(); }\nobject M() => new StreamReader(Root);", false)]
    [InlineData("string Root { get; }\nC(string p) { Root = p; }\nobject M() => new StreamReader(Root);", true)]
    [InlineData("static string Root { get; set; } = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("public static string Root { get; private set; } = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("string Root { get; init; } = AppContext.BaseDirectory;\nobject M() => new StreamReader(Root);", true)]
    [InlineData("static string Root => Environment.MachineName;\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("static string Root { get { return Environment.MachineName; } }\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("static string Root { get => field + \"|\"; } = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("protected virtual string Root { get; } = AppContext.BaseDirectory;\nobject M() => new StreamReader(Root);", true)]
    public void AGetOnlyAutoPropertyThatOnlyHoldsValidPathsIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-201: another object's field or property is read through its type's own writes, a generic type's through its
    /// definition and a sealed type's override auto-property through that override; a struct's is not read, since
    /// <c>this = ...</c> in any of its members writes every field.
    /// </summary>
    [Theory]
    [InlineData("sealed class Helper { public string TemporaryPath { get; } public Helper() { TemporaryPath = Path.Combine(Path.GetTempPath(), \"repo1\"); } }\nclass C\n{\nstatic object M(Helper h) => new StreamReader(h.TemporaryPath);\n}\n", false)]
    [InlineData("class H<T> { public readonly string F; public H() { F = AppContext.BaseDirectory; } }\nclass C\n{\nstatic object M(H<int> h) => new StreamReader(h.F);\n}\n", false)]
    [InlineData("class H<T> { public readonly string F; public H(string p) { F = p; } }\nclass C\n{\nstatic object M(H<int> h) => new StreamReader(h.F);\n}\n", true)]
    [InlineData("abstract class B { public abstract string Root { get; } }\nsealed class C : B\n{\npublic override string Root { get; } = AppContext.BaseDirectory;\nobject M() => new StreamReader(Root);\n}\n", false)]
    [InlineData("struct S { public readonly string F; public S(int n) { F = AppContext.BaseDirectory; } }\nclass C\n{\nstatic object M(S s) => new StreamReader(s.F);\n}\n", true)]
    [InlineData("partial class C\n{\npublic partial string Root { get; }\npublic partial string Root { get => field + \"|\"; }\nC() { Root = AppContext.BaseDirectory; }\nobject M() => new StreamReader(Root);\n}\n", true)]
    public void AnotherTypesFieldIsReadThroughItsOwnWrites(string types, bool flagged) =>
        Assert.Equal(flagged, Calls(Source("using System;\nusing System.IO;\n" + types)).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>Ticket P2-201: a field declared in a project the caller's references as source is read with that project's compilation.</summary>
    [Fact]
    public void AFieldInAnotherProjectIsRead()
    {
        Compilation library = RoslynTestCompilations.Compile(
            "public class TestHelper { public readonly string WorkingDir = System.AppContext.BaseDirectory; }", "Library");
        Compilation app = RoslynTestCompilations.Compile(
            "class C { static object M(TestHelper h) => new System.IO.StreamReader(System.IO.Path.Combine(h.WorkingDir, \"TAGMESSAGE\")); }", [library.ToMetadataReference()], "App");
        IMethodSymbol method = app.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();

        Assert.DoesNotContain(Calls(IrLowerer.Lower(method, app, RenameMap.Empty, [], Runtimes.Migration)), static call => call.Callee.RuntimeChanged);
    }

    private const string GetFullPath = "System.IO.Path::GetFullPath(string)";

    private const string CreateDirectory = "System.IO.Directory::CreateDirectory(string)";

    private static bool Flags(string member) => Flagged(member).Length > 0;

    /// <summary>The members of the calls <c>M</c> makes that a row flags, in order of their names.</summary>
    private static string[] Flagged(string member) =>
        [.. Calls(Source($"using System;\nusing System.IO;\nclass C\n{{\n{member}\n}}\n")).Where(static call => call.Callee.RuntimeChanged)
            .Select(static call => call.Callee.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
