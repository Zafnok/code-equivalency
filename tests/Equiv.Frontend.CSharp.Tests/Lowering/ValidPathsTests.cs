using System.Linq;

using Equiv.Core.Ir;

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

    private static bool Flags(string member) =>
        Calls(Source($"using System;\nusing System.IO;\nclass C\n{{\n{member}\n}}\n")).Any(static call => call.Callee.RuntimeChanged);
}
