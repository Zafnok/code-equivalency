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

    /// <summary>
    /// Ticket P2-230: a primary constructor's parameter is the type's state, which any member can write and a call between
    /// the guard and the use can run, so a guard on it does not clear the use; one on the method's own parameter does.
    /// </summary>
    [Theory]
    [InlineData("class C(string p)\n{\nvoid Reset() { p = \"a|b\"; }\nvoid M() { if (File.Exists(p)) { Reset(); File.Delete(p); } }\n}\n", true)]
    [InlineData("class C(string p)\n{\nvoid Reset() { p = \"a|b\"; }\nvoid M() { if (!File.Exists(p)) return; File.Delete(p); }\n}\n", true)]
    [InlineData("class C(string q)\n{\nvoid Reset() { q = \"a|b\"; }\nvoid M(string p) { if (File.Exists(p)) { Reset(); File.Delete(p); } }\n}\n", false)]
    public void AGuardOnAPrimaryConstructorsParameterDoesNotClearItsUse(string type, bool flagged) =>
        Assert.Equal(flagged, Calls(Source("using System.IO;\n" + type, "M")).Any(static call => call.Callee.RuntimeChanged));

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
    [InlineData("internal static string F = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(F);", true)]
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
    /// Ticket P2-228: a private field of a class whose type is a reference type is a valid path when its initializer and every
    /// write of it anywhere in its type, a getter that fills it on first use among them, is one: nothing outside the type can
    /// name it, and a read before the first write sees null. A write from a parameter, a <c>ref</c> to it that the type hands
    /// out, a write by a name that binds to no symbol, a field another type can name, and a value-type field, whose default
    /// is no path part, still fire the row.
    /// </summary>
    [Theory]
    [InlineData("string f;\nvoid Fill() { f = Path.GetTempPath(); }\nobject M() => new StreamReader(f);", false)]
    [InlineData("static string F = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(F);", false)]
    [InlineData("string cache;\nreadonly object gate = new();\nstring Dir { get { if (cache is not null) { return cache; } lock (gate) { if (cache is null) { var dir = Path.Combine(AppContext.BaseDirectory, \"x\"); cache = dir; } } return cache; } }\nobject M() => new StreamReader(Dir);", false)]
    [InlineData("static bool ready;\nstatic string support;\nstatic void Init() { support = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), \"OpenRA\") + Path.DirectorySeparatorChar; ready = true; }\nstatic string Support { get { if (!ready) { Init(); } return support; } }\nstatic object M() => new StreamReader(Path.Combine(Support, \"settings.yaml\"));", false)]
    [InlineData("string f;\nsealed class Nested { void Set(C c) { c.f = Path.GetTempPath(); } }\nobject M() => new StreamReader(f);", false)]
    [InlineData("string f = AppContext.BaseDirectory;\nint Count(C other) => f.Length + this.f.Length + other?.f.Length ?? 0;\nstring Name() => nameof(f);\nobject M() => new StreamReader(f);", false)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Each(string[] fs) { foreach (var f in fs) { GC.KeepAlive(f); } }\nobject M() => new StreamReader(f);", false)]
    [InlineData("string f;\nvoid Fill() { f = Path.GetTempPath(); }\nvoid Set(string p) { f = p; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f;\nC(string p) { f = p; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f;\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Append() { f += \"|\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Fill() { f ??= Path.GetTempPath(); }\nobject M() => new StreamReader(f);", true)]
    [InlineData("static void Fill(ref string s) { }\nstring f = AppContext.BaseDirectory;\nvoid Touch() { Fill(ref f); }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Swap() { f = System.Threading.Interlocked.Exchange(ref f, Path.GetTempPath()); }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nref string Slot() => ref f;\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nref string Slot() { return ref f; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nunsafe void Touch() { fixed (string* p = &f) { *p = \"a|b\"; } }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Touch() { TypedReference r = __makeref(f); __refvalue(r, string) = \"a|b\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("string f = AppContext.BaseDirectory;\nvoid Touch() { ((dynamic)this).f = \"a|b\"; }\nobject M() => new StreamReader(f);", true)]
    [InlineData("internal string f = AppContext.BaseDirectory;\nobject M() => new StreamReader(f);", true)]
    [InlineData("protected string f = AppContext.BaseDirectory;\nobject M() => new StreamReader(f);", true)]
    [InlineData("char sep;\nvoid Init() { sep = Path.DirectorySeparatorChar; }\nobject M() => new StreamReader(AppContext.BaseDirectory + sep);", true)]
    public void APrivateFieldThatOnlyHoldsValidPathsIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-228: a struct's private field is not read (<c>this = ...</c> writes every field), and nor is a field of a class
    /// with a <c>StructLayout</c>, where an explicit layout can lay two fields over each other so that writing one writes the
    /// other; that holds for P2-201's <c>readonly</c> field too.
    /// </summary>
    [Theory]
    [InlineData("struct C\n{\nstring f;\nvoid Fill() { f = Path.GetTempPath(); }\nobject M() => new StreamReader(f);\n}\n")]
    [InlineData("[StructLayout(LayoutKind.Explicit)]\nclass C\n{\n[FieldOffset(0)] string f;\n[FieldOffset(0)] string g;\nvoid Fill() { f = Path.GetTempPath(); g = \"a|b\"; }\nobject M() => new StreamReader(f);\n}\n")]
    [InlineData("[StructLayout(LayoutKind.Explicit)]\nclass C\n{\n[FieldOffset(0)] readonly string f = AppContext.BaseDirectory;\n[FieldOffset(0)] string g;\nvoid Touch() { g = \"a|b\"; }\nobject M() => new StreamReader(f);\n}\n")]
    public void AFieldAnotherWriteCanOverlayIsNotRead(string type) => Assert.Contains(
        Calls(Source("using System;\nusing System.IO;\nusing System.Runtime.InteropServices;\n" + type)), static call => call.Callee.RuntimeChanged);

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

    /// <summary>
    /// Ticket P2-232: a project referenced as source is seen through retargeting symbols when it was compiled against another
    /// version of an assembly the caller's project references (Git Extensions' <c>GitUI</c> sees <c>GitCommands</c> so). The
    /// member read is matched to the writes in its own project by its declaration, not by symbol identity.
    /// </summary>
    [Fact]
    public void AStoredPathInAProjectSeenThroughRetargetingIsRead()
    {
        Compilation oldDependency = RoslynTestCompilations.Compile("[assembly: System.Reflection.AssemblyVersion(\"1.0.0.0\")]\npublic sealed class Dependency { }", "Dependency");
        Compilation newDependency = RoslynTestCompilations.Compile("[assembly: System.Reflection.AssemblyVersion(\"2.0.0.0\")]\npublic sealed class Dependency { }", "Dependency");
        Compilation library = RoslynTestCompilations.Compile(
            "public sealed class Module { public Module(Dependency d) { WorkingDir = System.AppContext.BaseDirectory; } public string WorkingDir { get; } }", [oldDependency.ToMetadataReference()], "Library");
        Compilation app = RoslynTestCompilations.Compile(
            "class C { static object M(Module m) => new System.IO.StreamReader(m.WorkingDir); }", [library.ToMetadataReference(), newDependency.ToMetadataReference()], "App");
        IMethodSymbol method = app.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single();
        CompilationReference reference = app.References.OfType<CompilationReference>().Single(reference => reference.Compilation == library);

        Assert.False(SymbolEqualityComparer.Default.Equals(app.GetAssemblyOrModuleSymbol(reference), library.Assembly));
        Assert.DoesNotContain(Calls(IrLowerer.Lower(method, app, RenameMap.Empty, [], Runtimes.Migration)), static call => call.Callee.RuntimeChanged);
    }

    /// <summary>
    /// Ticket P2-217: a listed guard on a field or auto-property read guards a later read of the same member on the same
    /// receiver (<c>this</c>, a parameter nothing assigns, or none for a static member) when nothing between them runs code
    /// or writes the member, as Duplicati's <c>TearDown</c> and <c>TempFile.Dispose</c> do.
    /// </summary>
    [Theory]
    [InlineData("readonly string f = Environment.MachineName;\nvoid M() { if (File.Exists(this.f)) { File.Delete(this.f); } }", false)]
    [InlineData("string f;\nbool keep;\nvoid M() { try { if (!keep && f != null && File.Exists(f)) File.Delete(f); f = null; } catch { } }", false)]
    [InlineData("static string F;\nstatic void M() { if (File.Exists(F)) File.Delete(F); }", false)]
    [InlineData("string f;\nvoid M() { if (!File.Exists(f)) return; File.Delete(f); }", false)]
    [InlineData("string f;\nbool M() => File.Exists(f) && new StreamReader(f).EndOfStream;", false)]
    [InlineData("string f;\nobject M() => File.Exists(f) ? new StreamReader(f) : null;", false)]
    [InlineData("string f;\nstring g;\nvoid M(int n, string[] a) { if (!File.Exists(f)) return; n = -n + 1; var b = new int[n]; a[0] = (string)(object)g; g = null; _ = n > 0 ? b : null; File.Delete(f); }", false)]
    [InlineData("string f;\nvoid M(bool b) { if (File.Exists(f) && b) { File.Delete(f); } }", false)]
    [InlineData("string Root { get; set; }\nvoid M() { if (File.Exists(Root)) File.Delete(Root); }", false)]
    [InlineData("string f;\nvoid M(C c) { if (File.Exists(c.f)) File.Delete(c.f); }", false)]
    [InlineData("string f;\nvoid Reset() { f = \"a|b\"; }\nvoid M() { if (File.Exists(f)) { Reset(); File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M() { if (File.Exists(f)) { GC.KeepAlive(this); File.Delete(f); } }", true)]
    [InlineData("string f;\nbool Check() { f = \"a|b\"; return true; }\nvoid M() { if (File.Exists(f) && Check()) File.Delete(f); }", true)]
    [InlineData("string f;\nvoid M(string p) { if (File.Exists(f)) { f = p; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(C c) { if (File.Exists(f)) { c.f = \"a|b\"; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(System.Threading.Tasks.Task t) { if (File.Exists(f)) { t.Wait(); File.Delete(f); } }", true)]
    [InlineData("string f;\nasync System.Threading.Tasks.Task M(System.Threading.Tasks.Task t) { if (File.Exists(f)) { await t; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(int n) { if (File.Exists(f)) { for (var i = 0; i < n; i++) { File.Delete(f); } } }", true)]
    [InlineData("string f;\nvoid M() { ref string r = ref f; if (File.Exists(f)) { r = \"a|b\"; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(ref string r) { if (File.Exists(f)) { r = \"a|b\"; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(int n) { L: if (File.Exists(f)) { File.Delete(f); n--; if (n > 0) goto L; } }", true)]
    [InlineData("string f;\nstatic string S;\nvoid M() { if (File.Exists(f)) { var s = S; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(C other) { if (File.Exists(f)) File.Delete(other.f); }", true)]
    [InlineData("string f;\nC next;\nvoid M() { if (File.Exists(next.f)) File.Delete(next.f); }", true)]
    [InlineData("string f;\nvoid M(C c) { if (File.Exists(c.f)) { c = this; File.Delete(c.f); } }", true)]
    [InlineData("string f;\nC g;\nvoid M(ref C c) { if (File.Exists(c.f)) { g = null; File.Delete(c.f); } }", true)]
    [InlineData("string f;\nvoid M(string p) { if (File.Exists(p)) File.Delete(f); }", true)]
    [InlineData("string f;\nstring Root => f;\nvoid M() { if (File.Exists(Root)) File.Delete(Root); }", true)]
    [InlineData("protected virtual string Root { get; set; }\nvoid M() { if (File.Exists(Root)) File.Delete(Root); }", true)]
    [InlineData("static void M(Uri u) { if (!File.Exists(u.LocalPath)) return; File.Delete(u.LocalPath); }", true)]
    [InlineData("string f;\nstring Reset() { f = \"a|b\"; return AppContext.BaseDirectory; }\nobject M() => File.Exists(f) ? Path.Combine(path2: Reset(), path1: f) : null;", true)]
    [InlineData("string f;\npublic static C operator !(C c) => c;\nvoid M(C c) { if (File.Exists(f)) { c = !c; File.Delete(f); } }", true)]
    [InlineData("string f;\nvoid M(bool b) { if (b && f != null) File.Delete(f); }", true)]
    [InlineData("string f;\nstring Root { get; set; }\nvoid M() { if (File.Exists(f)) { Root = \"a|b\"; File.Delete(f); } }", true)]
    [InlineData("string f;\npublic static C operator +(C a, C b) => a;\nvoid M(C c) { if (File.Exists(f)) { c = c + c; File.Delete(f); } }", true)]
    [InlineData("string f;\npublic static implicit operator int(C c) => 0;\nvoid M(int n) { if (File.Exists(f)) { n = this; File.Delete(f); } }", true)]
    public void AGuardedReadOfAMemberThatNothingRewritesIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-217, Duplicati's <c>SingleInstance</c> shape: an auto-property of a parameter is guarded by an early exit
    /// on the same parameter's property.
    /// </summary>
    [Theory]
    [InlineData("static void M(Event e) { if (!File.Exists(e.FullPath)) return; File.Delete(e.FullPath); }", false)]
    [InlineData("static void M(Event e, Event other) { if (!File.Exists(other.FullPath)) return; File.Delete(e.FullPath); }", true)]
    public void AGuardedAutoPropertyOfAParameterIsAValidPath(string method, bool flagged) => Assert.Equal(flagged, Calls(Source(
        "using System.IO;\nsealed class Event { public string FullPath { get; } }\nclass C\n{\n" + method + "\n}\n")).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>
    /// Ticket P2-217: a struct's <c>this</c> can be a field of another object, which a plain assignment replaces between the
    /// guard and the use, so a guard on its field is not followed.
    /// </summary>
    [Fact]
    public void AGuardedFieldOfAStructIsNotAValidPath() => Assert.Contains(Calls(Source(
        "using System.IO;\nstruct C\n{\nstring f;\nvoid M() { if (File.Exists(f)) File.Delete(f); }\n}\n")), static call => call.Callee.RuntimeChanged);

    /// <summary>
    /// Ticket P2-207: a local or parameter read after a statement of an enclosing block that is a plain assignment to it (or
    /// its declaration with a value) holds that value when nothing after that statement and up to the read writes it, and
    /// nothing writes it through a <c>ref</c>, from a lambda or local function, or past a <c>goto</c>.
    /// </summary>
    [Theory]
    [InlineData("static object M() { string d; d = AppContext.BaseDirectory; return new StreamReader(d); }", false)]
    [InlineData("static object M(string p) { var d = AppContext.BaseDirectory; var r = new StreamReader(d); d = p; return r; }", false)]
    [InlineData("static object M(string p) { var d = p; d = Path.GetTempPath(); GC.KeepAlive(p); return new StreamReader(d); }", false)]
    [InlineData("static object M(string p) { p = AppContext.BaseDirectory; return new StreamReader(p); }", false)]
    [InlineData("static object M(string p, bool b) { var d = p; d = AppContext.BaseDirectory; if (b) { return new StreamReader(d); } d = p; return null; }", false)]
    [InlineData("static object M(string p) { var d = p; d = Path.Combine(AppContext.BaseDirectory, \"x\"); d = Path.Combine(d, \"y\"); return new StreamReader(d); }", false)]
    [InlineData("static bool Get(out string s) { s = null; return true; }\nstatic object M() { string d; Get(out d); d = AppContext.BaseDirectory; return new StreamReader(d); }", false)]
    [InlineData("static object M() { var a = AppContext.BaseDirectory; var b = a; a = b; return new StreamReader(a); }", false)]
    [InlineData("static object M(string p) { var d = p; d = d + \"\"; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p, bool b) { var d = AppContext.BaseDirectory; if (b) d = p; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p, int n) { var d = AppContext.BaseDirectory; while (n-- > 0) { GC.KeepAlive(new StreamReader(d)); d = p; } return null; }", true)]
    [InlineData("static object M(string p) { string d = null; Action a = () => d = p; d = AppContext.BaseDirectory; a(); return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { string d = null; void Set() => d = p; d = AppContext.BaseDirectory; Set(); return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { string s = p; ref string r = ref s; r = AppContext.BaseDirectory; return new StreamReader(r); }", true)]
    [InlineData("ref struct S { public void Get(out string s) { s = null; } }\nstatic object M() { string d; new S().Get(out d); d = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static Span<char> Get(out string s) { s = null; return default; }\nstatic object M() { string d; Get(out d); d = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static void Get(Span<char> c, out string s) { s = null; }\nstatic object M() { string d; Get(default, out d); d = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { string d = p; ref string r = ref d; d = AppContext.BaseDirectory; r = p; return new StreamReader(d); }", true)]
    [InlineData("static void Fill(ref string s) { }\nstatic object M(string p) { string d = p; Fill(ref d); d = AppContext.BaseDirectory; return new StreamReader(d); }", true)]
    [InlineData("static ref string Stash([System.Diagnostics.CodeAnalysis.UnscopedRef] out string s) { s = null; return ref s; }\nstatic object M(string p) { string d; ref string r = ref Stash(out d); d = AppContext.BaseDirectory; r = p; return new StreamReader(d); }", true)]
    [InlineData("static object M(ref string p) { p = AppContext.BaseDirectory; return new StreamReader(p); }", true)]
    [InlineData("static object M(string p, bool b) { string d = p; if (b) goto L; d = AppContext.BaseDirectory; L: return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { string d = AppContext.BaseDirectory, e = d = p; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p, int n) { var a = AppContext.BaseDirectory; var b = a; while (n-- > 0) { a = b; b = p; } return new StreamReader(a); }", true)]
    public void TheLastPlainWriteBeforeAReadIsWhatTheReadSees(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-207, Duplicati's <c>RecoveryTool.Index::Run</c> shape: a local first written as an <c>out</c> argument, then
    /// under an <c>if</c>, then last by a validator, holds the validator's result after it, so only the validator's row fires.
    /// </summary>
    [Fact]
    public void ALocalLastWrittenByAValidatorIsAValidPathAfterIt() => Assert.Equal([GetFullPath], Flagged(
        "static object M(System.Collections.Generic.Dictionary<string, string> options) { string ixfile; options.TryGetValue(\"indexfile\", out ixfile); if (string.IsNullOrWhiteSpace(ixfile)) ixfile = \"index.txt\"; ixfile = Path.GetFullPath(ixfile); var n = options.Count; foreach (var o in options) { n--; } return new StreamWriter(ixfile); }"));

    /// <summary>
    /// Ticket P2-207: a parameter of a private method that nothing assigns is a valid path when the method is only ever
    /// called, never referred to otherwise, and every call in its type passes a valid path, each call a step toward the
    /// bound of four. A path that really comes from a caller's own caller still fires the row.
    /// </summary>
    [Theory]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() => M(AppContext.BaseDirectory);", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() => M(Path.GetTempPath());\nstatic object Other() => C.M(Path.Combine(AppContext.BaseDirectory, \"x\"));", false)]
    [InlineData("object M(string file) => new StreamWriter(file);\nstatic object Run(C c) => c?.M(AppContext.BaseDirectory);", false)]
    [InlineData("static object M<T>(string file) => new StreamWriter(file);\nstatic object Run() => M<int>(AppContext.BaseDirectory);", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nsealed class Inner { static object Run() => M(AppContext.BaseDirectory); }", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() { string ixfile; ixfile = Path.GetFullPath(\"index.txt\"); return M(ixfile); }", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\npublic static object Run(string p) => M(p);", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run(string p) => M(AppContext.BaseDirectory) ?? M(p);", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() => M(\"index.txt\");", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() => M(\"index|txt\");", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run() { Func<string, object> f = M; return M(AppContext.BaseDirectory); }", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic string Name => nameof(M);\nstatic object Run() => M(AppContext.BaseDirectory);", true)]
    [InlineData("internal static object M(string file) => new StreamWriter(file);\nstatic object Run() => M(AppContext.BaseDirectory);", true)]
    [InlineData("static object M(string file) { if (file.Length == 0) file = Environment.MachineName; return new StreamWriter(file); }\nstatic object Run() => M(AppContext.BaseDirectory);", true)]
    [InlineData("static object M(ref string file) => new StreamWriter(file);\nstatic object Run() { var f = AppContext.BaseDirectory; return M(ref f); }", true)]
    [InlineData("static object M(string file, int n) => n > 0 ? M(file, n - 1) : new StreamWriter(file);\nstatic object Run() => M(AppContext.BaseDirectory, 2);", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run(string p) { var d = AppContext.BaseDirectory; Func<object> f = () => M(d); d = p; return f(); }", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object Run(string p) { var d = AppContext.BaseDirectory; object Call() => M(d); d = p; return Call(); }", true)]
    [InlineData("object M(string file) => new StreamWriter(file);\nobject Run() { dynamic d = this; d.M(Environment.MachineName); return M(AppContext.BaseDirectory); }", true)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object M2(string a) => M3(a);\nstatic object M3(string b) => M4(b);\nstatic object M4(string c) => M(c);\nstatic object Run() => M2(AppContext.BaseDirectory);", false)]
    [InlineData("static object M(string file) => new StreamWriter(file);\nstatic object M1(string z) => M2(z);\nstatic object M2(string a) => M3(a);\nstatic object M3(string b) => M4(b);\nstatic object M4(string c) => M(c);\nstatic object Run() => M1(AppContext.BaseDirectory);", true)]
    public void EveryCallOfAPrivateMethodPassesItsParameter(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-207: an extension method called as an instance method is one of its calls; a method the compiler can call
    /// by a name the source does not write, a constructor and a primary constructor's parameter, which other members write,
    /// are not read.
    /// </summary>
    [Theory]
    [InlineData("static class C\n{\nstatic object M(this string file) => new StreamWriter(file);\nstatic object Run() => AppContext.BaseDirectory.M();\n}\n", "M", false)]
    [InlineData("static class C\n{\nstatic object M(this string file) => new StreamWriter(file);\nstatic object Run(string p) => AppContext.BaseDirectory.M() ?? p.M();\n}\n", "M", true)]
    [InlineData("class C\n{\nobject Add(string file) => new StreamWriter(file);\nobject Run() => Add(AppContext.BaseDirectory);\n}\n", "Add", true)]
    [InlineData("class C\n{\nprivate C(string file) { GC.KeepAlive(new StreamWriter(file)); }\nstatic C Run() => new C(AppContext.BaseDirectory);\n}\n", ".ctor", true)]
    [InlineData("class C(string p)\n{\nvoid Reset() { p = \"a|b\"; }\nobject M() { p = AppContext.BaseDirectory; Reset(); return new StreamReader(p); }\n}\n", "M", true)]
    public void OnlyCallsTheSourceNamesAreRead(string type, string method, bool flagged) =>
        Assert.Equal(flagged, Calls(Source("using System;\nusing System.IO;\n" + type, method)).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>Ticket P2-207: a name that is another overload or a parameter of the same name is not a call of the method.</summary>
    [Fact]
    public void AnotherSymbolOfTheSameNameIsNotACall() => Assert.DoesNotContain(Calls(Source(
        "using System;\nusing System.IO;\nclass C\n{\nstatic object M(string file) => new StreamWriter(file);\nstatic object M(int n, int m) => null;\nstatic object Run(string M) => C.M(M.Length, 0) ?? C.M(AppContext.BaseDirectory);\n}\n",
        parameters: 1)), static call => call.Callee.RuntimeChanged);

    private const string GetFullPath = "System.IO.Path::GetFullPath(string)";

    private const string CreateDirectory = "System.IO.Directory::CreateDirectory(string)";

    /// <summary>
    /// Ticket P2-208: a local whose writes give it valid paths when it already holds one only ever holds valid paths, so
    /// <c>a</c> and <c>b</c> copied into each other in a loop are, and a write that joins an invalid character to it is not.
    /// </summary>
    [Theory]
    [InlineData("static object M(int n) { var a = AppContext.BaseDirectory; var b = a; while (n-- > 0) { b = a; a = b; } return new StreamReader(a); }", false)]
    [InlineData("static object M(int n) { var d = AppContext.BaseDirectory; while (n-- > 0) { d = d + \"x\"; } return new StreamReader(d); }", false)]
    [InlineData("static object M(int n) { var d = AppContext.BaseDirectory; while (n-- > 0) { d = d + \"|\"; } return new StreamReader(d); }", true)]
    public void ALocalEveryWriteOfWhichKeepsItValidIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-208: a call to a local function that reads no local variable or parameter of a method around it is read as
    /// a solution callee: its body is the one that runs. One that reads them (a parameter, a valid local, or a parameter a
    /// guard covers only after the call), or that is passed the path, still fires the row.
    /// </summary>
    [Theory]
    [InlineData("static object M() { return new StreamReader(Dir()); string Dir() => AppContext.BaseDirectory; }", false)]
    [InlineData("static object M() { return new StreamReader(Dir()); static string Dir() { var t = Path.GetTempPath(); return Path.Combine(t, Path.GetRandomFileName()); } }", false)]
    [InlineData("static object M() { return new StreamReader(Dir()); string Dir() { Func<string, string> f = s => s; GC.KeepAlive(f); return AppContext.BaseDirectory; } }", false)]
    [InlineData("static object M(string p) { return new StreamReader(Dir()); string Dir() => p; }", true)]
    [InlineData("static object M(string p) { var r = new StreamReader(Dir()); if (!File.Exists(p)) return null; return r; string Dir() => p; }", true)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; return new StreamReader(Dir()); string Dir() => d; }", true)]
    [InlineData("static object M(string p) { return new StreamReader(Dir(p)); static string Dir(string q) => q; }", true)]
    [InlineData("static object M() { return new StreamReader(Dir()); static string Dir() { throw new InvalidOperationException(); } }", true)]
    public void ALocalFunctionThatReadsNothingAroundItIsReadAsACallee(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-208: a valid path followed by a string constant with no character <c>invalidPath</c> names, colon included,
    /// is one; a constant before a valid path is not, since the path's drive colon would move off its place.
    /// </summary>
    [Theory]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + \"a.txt\");", false)]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + \"a|b\");", true)]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + \":x\");", true)]
    [InlineData("static object M() => new StreamReader(\"x\" + AppContext.BaseDirectory);", true)]
    [InlineData("static object M() => new StreamReader(\"x\" + Path.GetTempPath());", true)]
    [InlineData("static object M(string p) => new StreamReader(p + \"a.txt\");", true)]
    [InlineData("static object M(string p) => new StreamReader(AppContext.BaseDirectory + p);", true)]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + 1);", true)]
    [InlineData("sealed class Q { public static string operator +(Q q, string s) => s; }\nstatic object M() => new StreamReader(new Q() + \"a.txt\");", true)]
    [InlineData("sealed class Q { public static string operator -(Q q, string s) => s; }\nstatic object M() => new StreamReader(new Q() - \"a.txt\");", true)]
    public void AConstantAfterAValidPathIsOne(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-208, Git Extensions' <c>GetTemporaryPath</c> shape: a constant before a valid path is one where a
    /// <c>StartsWith</c> has returned true for the path on a constant that begins with a separator and an ASCII letter or
    /// digit, so the path has no drive colon, and nothing between that guard and the read runs code or writes the path.
    /// </summary>
    [Theory]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"/var\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", false)]
    [InlineData("static object M(string p) => File.Exists(p) && p.StartsWith(\"/var\", StringComparison.Ordinal) ? new StreamReader(\"/private\" + p) : null;", false)]
    [InlineData("static object M(string p) { var t = p; if (t.StartsWith(\"/var\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (!t.StartsWith(\"/var\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"C:\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"/\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"/-\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"v/\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M(string q) { var t = Path.GetTempPath(); if (t.StartsWith(q, StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith('/')) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.EndsWith(\"/var\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var s = Path.GetTempPath(); var t = s; if (s.StartsWith(\"/var\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var s = Path.GetTempPath(); var t = s; if (t.StartsWith(\"/var\", StringComparison.Ordinal)) { t = s; t = \"/private\" + t; } return new StreamReader(t); }", true)]
    [InlineData("static object M() { var s = Path.GetTempPath(); ref string t = ref s; if (t.StartsWith(\"/var\", StringComparison.Ordinal)) { s = \"/private\" + t; } return new StreamReader(s); }", true)]
    [InlineData("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"\\\\tmp\", StringComparison.Ordinal)) { t = \"/private\" + t; } return new StreamReader(t); }", false)]
    [InlineData("static readonly string F = Path.GetTempPath();\nstatic object M() => F.StartsWith(\"/var\", StringComparison.Ordinal) ? new StreamReader(\"/private\" + F) : null;", true)]
    [InlineData("static readonly string F = Path.GetTempPath();\nstatic object M() { ref readonly string t = ref F; return t.StartsWith(\"/var\", StringComparison.Ordinal) ? new StreamReader(\"/private\" + t) : null; }", true)]
    [InlineData("static object M(ref string p) => File.Exists(p) && p.StartsWith(\"/var\", StringComparison.Ordinal) ? new StreamReader(\"/private\" + p) : null;", true)]
    [InlineData("static object M(string p) { p = Path.GetTempPath(); var s = p; if (p.StartsWith(\"/var\", StringComparison.Ordinal)) { p = s; return new StreamReader(\"/private\" + p); } return null; }", true)]
    [InlineData("sealed class P { public bool StartsWith(string s) => true; }\nstatic object M() { var t = Path.GetTempPath(); if (new P().StartsWith(\"/var\")) { t = \"/private\" + t; } return new StreamReader(t); }", true)]
    public void AConstantBeforeAValidPathIsOneWhereAGuardRulesOutADrive(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-208: the corpus's <c>StartsWith("/var")</c> compares by culture, which equates a colon with no letter or
    /// digit either, so the joined path is valid; the <c>StartsWith</c> call's own culture row still fires.
    /// </summary>
    [Fact]
    public void ACultureStartsWithRulesOutADriveToo() => Assert.Equal(
        ["System.String::StartsWith(string)"],
        Flagged("static object M() { var t = Path.GetTempPath(); if (t.StartsWith(\"/var\")) { t = \"/private\" + t; } return new StreamReader(t); }"));

    /// <summary>
    /// Ticket P2-208, Git Extensions #51's <c>GitModuleTestHelper.TemporaryPath</c>: a get-only auto-property its constructor
    /// sets from a local function that joins the temp directory, maybe behind <c>/private</c>, to a random file name. The
    /// same local function on a path the constructor was passed still fires the row.
    /// </summary>
    [Theory]
    [InlineData("Path.GetTempPath()", false)]
    [InlineData("root", true)]
    public void ATemporaryPathSetFromALocalFunctionIsAValidPath(string start, bool flagged) => Assert.Equal(flagged, Calls(Source(
        "using System;\nusing System.IO;\n"
        + "sealed class Helper\n{\n"
        + "public Helper(string root)\n{\nTemporaryPath = GetTemporaryPath();\nreturn;\n"
        + "string GetTemporaryPath()\n{\nvar tempPath = " + start + ";\n"
        + "if (tempPath.StartsWith(\"/var\"))\n{\ntempPath = \"/private\" + tempPath;\n}\n"
        + "return Path.Combine(tempPath, Path.GetRandomFileName());\n}\n}\n"
        + "public string TemporaryPath { get; }\n}\n"
        + "class C\n{\nstatic object M(Helper h) => Directory.CreateDirectory(Path.Combine(h.TemporaryPath, \"Folder1\"));\n}\n")).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>
    /// Ticket P2-232: a callee's returned parameter is a valid path when the call passes it one and every write of it in the
    /// callee keeps it one, the parameters so passed taken to hold valid paths. A parameter the call passes a path from its
    /// own caller, one the callee overwrites with anything else (in a lambda too), and a <c>ref</c> parameter still fire.
    /// </summary>
    [Theory]
    [InlineData("static string Ensure(string d) => d;\nstatic object M() => new StreamReader(Ensure(AppContext.BaseDirectory));", false)]
    [InlineData("static string Ensure(string d) { if (d.Length > 0) { d += \"x\"; } return d; }\nstatic object M() => new StreamReader(Ensure(AppContext.BaseDirectory));", false)]
    [InlineData("static string Pick(string a, string b) { a = b; return a; }\nstatic object M() => new StreamReader(Pick(AppContext.BaseDirectory, Path.GetTempPath()));", false)]
    [InlineData("static string Ensure(string d) => d;\nstatic object M(string p) => new StreamReader(Ensure(p));", true)]
    [InlineData("static string Ensure(string d) { d += \"|\"; return d; }\nstatic object M() => new StreamReader(Ensure(AppContext.BaseDirectory));", true)]
    [InlineData("static string Pick(string a, string b) { a = b; return a; }\nstatic object M(string p) => new StreamReader(Pick(AppContext.BaseDirectory, p));", true)]
    [InlineData("static string Ensure(string d) { Action a = () => d = Environment.MachineName; a(); return d; }\nstatic object M() => new StreamReader(Ensure(AppContext.BaseDirectory));", true)]
    [InlineData("static string Ensure(ref string d) => d;\nstatic object M() { var d = AppContext.BaseDirectory; return new StreamReader(Ensure(ref d)); }", true)]
    public void ACalleesReturnedParameterIsReadAsTheCallsArgument(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-232: a char joins a valid path as a constant with no rejected character and no colon does: a char constant,
    /// a framework directory separator, or a solution's <c>readonly</c> char field that only ever holds one; and a compound
    /// <c>+=</c> of one onto a valid local or parameter keeps it valid. A colon, the volume separator and a mutable field do not.
    /// </summary>
    [Theory]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += Path.DirectorySeparatorChar; return new StreamReader(d); }", false)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += Path.AltDirectorySeparatorChar; return new StreamReader(d); }", false)]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + Path.DirectorySeparatorChar);", false)]
    [InlineData("static object M() => new StreamReader(AppContext.BaseDirectory + 'x');", false)]
    [InlineData("static readonly char Sep = Path.DirectorySeparatorChar;\nstatic object M() { var d = AppContext.BaseDirectory; d += Sep; return new StreamReader(d); }", false)]
    [InlineData("static readonly char Sep;\nstatic C() { Sep = '/'; }\nstatic object M() { var d = AppContext.BaseDirectory; d += Sep; return new StreamReader(d); }", false)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += \"x\"; return new StreamReader(d); }", false)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += ':'; return new StreamReader(d); }", true)]
    [InlineData("static object M() { var d = AppContext.BaseDirectory; d += Path.VolumeSeparatorChar; return new StreamReader(d); }", true)]
    [InlineData("static char Sep = '/';\nstatic object M() { var d = AppContext.BaseDirectory; d += Sep; return new StreamReader(d); }", true)]
    [InlineData("static readonly char Sep = ':';\nstatic object M() { var d = AppContext.BaseDirectory; d += Sep; return new StreamReader(d); }", true)]
    [InlineData("static readonly char Sep;\nstatic object M() { var d = AppContext.BaseDirectory; d += Sep; return new StreamReader(d); }", true)]
    [InlineData("static object M(char c) { var d = AppContext.BaseDirectory; d += c; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { var d = AppContext.BaseDirectory; d += p; return new StreamReader(d); }", true)]
    [InlineData("static object M(string p) { var d = AppContext.BaseDirectory; d ??= p; return new StreamReader(d); }", true)]
    [InlineData("sealed class Q { public static implicit operator string(Q q) => \"x\"; }\nstatic object M() { var d = AppContext.BaseDirectory; d += (string)new Q(); return new StreamReader(d); }", true)]
    public void ASeparatorAppendedToAValidPathKeepsItValid(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-232: a value that is a valid path or the empty string does not fire a row the empty string does not reach
    /// (<c>SetCurrentDirectory</c>, <c>Combine</c>), and still fires every other path row. A blank string that is not empty,
    /// or the empty string joined to one, is not such a value.
    /// </summary>
    [Theory]
    [InlineData("static object M(string p) { Directory.SetCurrentDirectory(p.Length == 0 ? string.Empty : AppContext.BaseDirectory); return null; }", false)]
    [InlineData("static object M(string p) { Directory.SetCurrentDirectory(p.Length == 0 ? \"\" : AppContext.BaseDirectory); return null; }", false)]
    [InlineData("static object M(string p) => Path.Combine(p.Length == 0 ? \"\" : AppContext.BaseDirectory, \"a.txt\");", false)]
    [InlineData("static object M(string p) { var d = p.Length == 0 ? \"\" : AppContext.BaseDirectory; d += Path.DirectorySeparatorChar; Directory.SetCurrentDirectory(d); return null; }", false)]
    [InlineData("static object M(string p) { var d = p.Length == 0 ? \"\" : AppContext.BaseDirectory; d += \"\"; Directory.SetCurrentDirectory(d); return null; }", false)]
    [InlineData("static object M(string p) => new StreamReader(p.Length == 0 ? \"\" : AppContext.BaseDirectory);", true)]
    [InlineData("static object M(string p) { Directory.SetCurrentDirectory(p.Length == 0 ? \" \" : AppContext.BaseDirectory); return null; }", true)]
    [InlineData("static object M(string p) { var d = p.Length == 0 ? \"\" : AppContext.BaseDirectory; d += \" \"; Directory.SetCurrentDirectory(d); return null; }", true)]
    [InlineData("static object M(string p) { Directory.SetCurrentDirectory(p.Length == 0 ? string.Empty : p); return null; }", true)]
    [InlineData("static object M(string p) => Path.GetDirectoryName(p.Length == 0 ? \"\" : AppContext.BaseDirectory);", true)]
    [InlineData("static readonly string Empty = \" \";\nstatic object M(string p) { Directory.SetCurrentDirectory(p.Length == 0 ? Empty : AppContext.BaseDirectory); return null; }", true)]
    public void AValidPathOrTheEmptyStringPassesOnlyTheRowsTheEmptyStringDoesNotReach(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-232, Git Extensions #55's <c>GitModule.WorkingDir</c>: a get-only auto-property set to a path normalised to
    /// the empty string or a full path, then given a trailing separator by an extension that returns its own parameter. It
    /// is a valid path or the empty string, so <c>SetCurrentDirectory</c> does not fire on it and <c>CreateDirectory</c> does;
    /// the same extension on the path the constructor was passed still fires.
    /// </summary>
    [Theory]
    [InlineData("(workingDir ?? \"\").NormalizePath()", "Directory.SetCurrentDirectory", false)]
    [InlineData("(workingDir ?? \"\").NormalizePath()", "Directory.CreateDirectory", true)]
    [InlineData("workingDir", "Directory.SetCurrentDirectory", true)]
    public void AWorkingDirectoryWithATrailingSeparatorIsAValidPathOrEmpty(string normalised, string call, bool flagged) => Assert.Equal(flagged, Calls(Source(
        "using System;\nusing System.IO;\n"
        + "static class PathUtil\n{\n"
        + "public static readonly char NativeDirectorySeparatorChar = Path.DirectorySeparatorChar;\n"
        + "public static readonly char PosixDirectorySeparatorChar = '/';\n"
        + "public static string EnsureTrailingPathSeparator(this string dirPath)\n{\n"
        + "if (!string.IsNullOrEmpty(dirPath) && dirPath[dirPath.Length - 1] != NativeDirectorySeparatorChar && dirPath[dirPath.Length - 1] != PosixDirectorySeparatorChar)\n{\n"
        + "dirPath += NativeDirectorySeparatorChar;\n}\nreturn dirPath;\n}\n"
        + "public static string NormalizePath(this string path)\n{\n"
        + "if (string.IsNullOrWhiteSpace(path))\n{\nreturn string.Empty;\n}\n"
        + "try\n{\nreturn Path.GetFullPath(path);\n}\ncatch (UriFormatException)\n{\nreturn string.Empty;\n}\n}\n}\n"
        + "sealed class GitModule\n{\n"
        + "public GitModule(string workingDir)\n{\nWorkingDir = " + normalised + ".EnsureTrailingPathSeparator();\n}\n"
        + "public string WorkingDir { get; }\n}\n"
        + "class C\n{\nstatic void M(GitModule m) => " + call + "(m.WorkingDir);\n}\n")).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>
    /// Ticket P2-232: an iterator's <c>yield break</c> returns no value, so a callee that is an iterator is read as returning
    /// only what it yields; the null failed the empty-string check. <c>WriteAllLines</c>'s row
    /// has no precondition, so its call lowers flagged; every argument is still read for a valid path.
    /// </summary>
    [Fact]
    public void AnIteratorsYieldBreakReturnsNoValue() => Assert.Equal(
        ["System.IO.File::WriteAllLines(string,System.Collections.Generic.IEnumerable<string>)"],
        Flagged("static System.Collections.Generic.IEnumerable<string> Lines() { yield break; }\nstatic void M() => File.WriteAllLines(AppContext.BaseDirectory + \"x\", Lines());"));

    /// <summary>
    /// Ticket P2-218: a string constant that does not meet <c>invalidPath</c> is a valid path wherever the rules read a value:
    /// a field that only ever holds one, a branch of a conditional, a callee's return. One that meets it still fires.
    /// </summary>
    [Theory]
    [InlineData("readonly string name = \"data.zip\";\nobject M() => new StreamReader(Path.Combine(AppContext.BaseDirectory, name));", false)]
    [InlineData("static object M(bool b) => new StreamReader(b ? \"a.txt\" : AppContext.BaseDirectory);", false)]
    [InlineData("static string Name() => \"a.txt\";\nstatic object M() => new StreamReader(Name());", false)]
    [InlineData("readonly string name = \"a|b\";\nobject M() => new StreamReader(Path.Combine(AppContext.BaseDirectory, name));", true)]
    [InlineData("readonly string name = \"\";\nobject M() => new StreamReader(name);", true)]
    [InlineData("static object M(bool b) => new StreamReader(b ? \"ab:c\" : AppContext.BaseDirectory);", true)]
    [InlineData("internal string name = \"data.zip\";\nobject M() => new StreamReader(Path.Combine(AppContext.BaseDirectory, name));", true)]
    public void AConstantThatDoesNotMeetInvalidPathIsAValidPath(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>
    /// Ticket P2-218, Duplicati #27's <c>CommandLineOperationsTests.zipAlternativeFilepath</c>: in an instance member of a
    /// class, a <c>static readonly</c> field that an instance field initializer of the class or a base class has already
    /// passed, unconditionally, to the same member as the same parameter is accepted by that member's validation, so the
    /// member's row does not fire on it. A field that can change, another member or parameter, a conditional or static
    /// initializer, a static member, and a site in a field initializer or a constructor initializer still fire.
    /// </summary>
    [Theory]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Base, zip);", false)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.GetFileName(Path.Combine(Base, \"data\"));", "object M() { return Join(); string Join() => Path.Combine(Base, \"x\"); }", false)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected string Data { get; } = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Base, zip);", false)]
    [InlineData("", "protected static string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Base, zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.GetDirectoryName(Base);", "object M() => Path.Combine(Base, zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(\"data\", Base);", "object M() => Path.Combine(Base, zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Base == null ? null : Path.Combine(Base, \"data\");", "object M() => Path.Combine(Base, zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected static readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Base, zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "static object M() => Path.Combine(Base, \"x\");", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Other, zip);\nstatic readonly string Other = Environment.GetEnvironmentVariable(\"Y\");", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.Combine(Environment.GetEnvironmentVariable(\"Y\"), zip);", true)]
    [InlineData("", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "object M() => Path.GetDirectoryName(Base);", true)]
    [InlineData(".ctor", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "readonly string Early = Path.Combine(Base, \"early\");\nC() { }", true)]
    [InlineData(".ctor", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");\nprotected Helper(string s) { }", "protected readonly string Data = Path.Combine(Base, \"data\");", "C() : base(Path.Combine(Base, \"x\")) { }", true)]
    [InlineData(".ctor", "protected static readonly string Base = Environment.GetEnvironmentVariable(\"X\");", "protected readonly string Data = Path.Combine(Base, \"data\");", "C() { GC.KeepAlive(Path.Combine(Base, \"x\")); }", false)]
    public void AStaticFieldTheObjectsInitializersPassedToTheSameMemberIsAccepted(string name, string field, string initializer, string member, bool flagged) => Assert.Equal(flagged, Calls(Source(
        "using System;\nusing System.IO;\n"
        + "abstract class Helper\n{\n" + field + "\n" + initializer + "\n}\n"
        + "sealed class C : Helper\n{\nreadonly string zip = \"a.zip\";\n" + member + "\n}\n", name: name is "" ? "M" : name)).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>Ticket P2-218: a struct's field initializers do not run for its default value, so a struct's member is not read so.</summary>
    [Fact]
    public void AStructsInitializersAreNotRead() => Assert.True(Calls(Source(
        "using System;\nusing System.IO;\n"
        + "struct C\n{\nstatic readonly string Base = Environment.GetEnvironmentVariable(\"X\");\nreadonly string data = Path.Combine(Base, \"data\");\n"
        + "public C() { }\nobject M() => Path.Combine(Base, \"x\");\n}\n")).Any(static call => call.Callee.RuntimeChanged));

    /// <summary>
    /// Ticket P2-231: the result of a member whose .NET Framework check is <c>Path.CheckInvalidPathChars</c>
    /// (<c>Path.GetFileName</c>, <c>Path.Combine</c>, <c>Path.GetDirectoryName</c>) passed that check, so a row whose own check
    /// is that one (<c>Path.Combine</c>) does not fire on it, through a local, a solution callee or a private method's only
    /// caller as on any valid path; the member's own row fires on its argument instead. A row whose check is wider
    /// (<c>StreamReader</c>, <c>Path.GetDirectoryName</c>) still fires, as a <c>?</c>, a <c>*</c> or a stray colon passes the
    /// character check. <c>ChangeExtension</c> does not check its extension, so its result has passed nothing, and a path that
    /// really comes from an argument still fires.
    /// </summary>
    [Theory]
    [InlineData("static object M(string p) => Path.Combine(AppContext.BaseDirectory, Path.GetFileName(p));", new[] { GetFileName })]
    [InlineData("static object M(string p) => new StreamReader(Path.GetFileName(p));", new[] { GetFileName, StreamReaderOfPath })]
    [InlineData("static object M(string p) => Path.Combine(AppContext.BaseDirectory, p);", new[] { Combine })]
    [InlineData("static object M(string p) => Path.Combine(p, Path.GetFileName(AppContext.BaseDirectory));", new[] { Combine })]
    [InlineData("static object M(string p, string q) => Path.Combine(Path.Combine(p, q), \"web\", \"index.html\");", new[] { Combine })]
    [InlineData("static object M(string p) { var name = Path.GetFileNameWithoutExtension(p); return Path.Combine(Path.GetTempPath(), name + \".bak\"); }", new[] { "System.IO.Path::GetFileNameWithoutExtension(string)" })]
    [InlineData("static object M(string p) => Path.Combine(Path.GetDirectoryName(p), Path.GetExtension(p));", new[] { GetDirectoryName, "System.IO.Path::GetExtension(string)" })]
    [InlineData("static object M(string p) => Path.GetDirectoryName(Path.GetFileName(p));", new[] { GetDirectoryName, GetFileName })]
    [InlineData("static object M(string e) => Path.Combine(AppContext.BaseDirectory, Path.ChangeExtension(Path.GetTempFileName(), e));", new[] { "System.IO.Path::ChangeExtension(string,string)", Combine })]
    [InlineData("static object M(string p, bool b) => Path.Combine(AppContext.BaseDirectory, b ? Path.GetFileName(p) : p);", new[] { Combine, GetFileName })]
    public void TheResultOfAMemberWhoseCheckIsTheCharacterCheckPassesARowWhoseCheckIsThatOne(string member, string[] flagged) =>
        Assert.Equal(flagged.Order(StringComparer.Ordinal), Flagged(member), StringComparer.Ordinal);

    /// <summary>
    /// Ticket P2-231, Git Extensions #38's <c>FileAssociatedIconProvider::CreateTempFile</c> shape: a private method whose only
    /// call passes <c>Path.GetFileName</c> of a path no rule shows valid joins it to the temporary directory, and its
    /// <c>Path.Combine</c> no longer fires; a <c>StreamWriter</c> of the parameter, or a call that passes the path itself, still
    /// does. Duplicati #30's <c>IndexHtmlHandler::Process</c> shape: a <c>Path.Combine</c> of a solution callee that returns a
    /// <c>Path.Combine</c> of a field set from a constructor's parameter passed the character check too.
    /// </summary>
    [Theory]
    [InlineData("static object M(string fileName) => Path.Combine(Path.GetTempPath(), fileName);\nstatic object Run(string fullPath) => M(Path.GetFileName(fullPath));", false)]
    [InlineData("static object M(string fileName) => new StreamWriter(fileName);\nstatic object Run(string fullPath) => M(Path.GetFileName(fullPath));", true)]
    [InlineData("static object M(string fileName) => Path.Combine(Path.GetTempPath(), fileName);\nstatic object Run(string fullPath) => M(Path.GetFileName(fullPath)) ?? M(fullPath);", true)]
    [InlineData("readonly string root;\nC(string r) { root = r; }\nstring GetPath(string u) => Path.Combine(root, u);\nobject M(string u) { var path = GetPath(u); return Path.Combine(path, \"index.html\"); }", false)]
    [InlineData("readonly string root;\nC(string r) { root = r; }\nstring GetPath(string u) => Path.Combine(root, u);\nobject M(string u) => new StreamReader(GetPath(u));", true)]
    [InlineData("readonly string root;\nC(string r) { root = r; }\nstring GetPath(string u) => root + u;\nobject M(string u) => Path.Combine(GetPath(u), \"index.html\");", true)]
    public void ACharacterCheckedPathIsFollowedAsAValidPathIs(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    private const string GetFileName = "System.IO.Path::GetFileName(string)";

    private const string GetDirectoryName = "System.IO.Path::GetDirectoryName(string)";

    private const string Combine = "System.IO.Path::Combine(string,string)";

    private const string StreamReaderOfPath = "System.IO.StreamReader::.ctor(string)";

    private static bool Flags(string member) => Flagged(member).Length > 0;

    /// <summary>The members of the calls <c>M</c> makes that a row flags, in order of their names.</summary>
    private static string[] Flagged(string member) =>
        [.. Calls(Source($"using System;\nusing System.IO;\nclass C\n{{\n{member}\n}}\n")).Where(static call => call.Callee.RuntimeChanged)
            .Select(static call => call.Callee.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
