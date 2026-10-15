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
    [InlineData("static string Inner() => AppContext.BaseDirectory;\nstatic string Outer() => Inner();\nstatic object M() => new StreamReader(Outer());", true)]
    [InlineData("static string Inner => AppContext.BaseDirectory;\nstatic string Outer => Inner;\nstatic object M() => new StreamReader(Outer);", true)]
    [InlineData("static string Root { get; } = AppContext.BaseDirectory;\nstatic object M() => new StreamReader(Root);", true)]
    [InlineData("static string Dir() { throw new InvalidOperationException(); }\nstatic object M() => new StreamReader(Dir());", true)]
    [InlineData("protected virtual string Dir() => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());", true)]
    [InlineData("static object M() { return new StreamReader(Dir()); string Dir() => AppContext.BaseDirectory; }", true)]
    [InlineData("static object M(Func<string> dir) => new StreamReader(dir());", true)]
    public void ASolutionMemberThatReturnsOnlyValidPathsYieldsOne(string member, bool flagged) => Assert.Equal(flagged, Flags(member));

    /// <summary>Ticket P2-188: a member an override can replace is not read, as an abstract or an overriding one shows.</summary>
    [Theory]
    [InlineData("interface I { string Dir(); }\nclass C\n{\nstatic object M(I i) => new StreamReader(i.Dir());\n}\n")]
    [InlineData("abstract class B { public abstract string Dir(); }\nclass C : B\n{\npublic override string Dir() => AppContext.BaseDirectory;\nobject M() => new StreamReader(Dir());\n}\n")]
    public void AMemberAnOverrideCanReplaceIsNotRead(string types) =>
        Assert.Contains(Calls(Source("using System;\nusing System.IO;\n" + types)), static call => call.Callee.RuntimeChanged);

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

    private const string GetFullPath = "System.IO.Path::GetFullPath(string)";

    private const string CreateDirectory = "System.IO.Directory::CreateDirectory(string)";

    private static bool Flags(string member) => Flagged(member).Length > 0;

    /// <summary>The members of the calls <c>M</c> makes that a row flags, in order of their names.</summary>
    private static string[] Flagged(string member) =>
        [.. Calls(Source($"using System;\nusing System.IO;\nclass C\n{{\n{member}\n}}\n")).Where(static call => call.Callee.RuntimeChanged)
            .Select(static call => call.Callee.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
