using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Ticket P2-190: six rows fire only in a method that makes a call to their member that can reach their documented change.
/// Each call that cannot is next to one like it that can, which still fires. Each case is checked in the lowering and in the
/// fingerprint, which must agree.
/// </summary>
public sealed class RowCallPreconditionsTests
{
    private const string OSVersion = "System.Environment::get_OSVersion(";
    private const string Ceiling = "System.Math::Ceiling(double)";
    private const string SetValue = "System.Reflection.FieldInfo::SetValue(";
    private const string GetString = "System.Text.Encoding::GetString(";
    private const string GetChars = "System.Text.Encoding::GetChars(";
    private const string GetCharCount = "System.Text.Encoding::GetCharCount(";
    private const string Start = "System.Diagnostics.Process::Start(";

    [Theory]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT;")]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.CompareTo(new Version(6, 0)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 1)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 2)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(minor: 2, major: 6)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version >= new Version(6, 1);")]
    [InlineData("return new Version(5, 1) < Environment.OSVersion.Version;")]
    public void AnOSVersionReadTheShimCannotChangeDoesNotFire(string body) =>
        Assert.False(Fires(body, OSVersion), body);

    [Theory]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 3)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version >= new Version(10, 0);")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 1, 7601)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(v) >= 0;")]
    [InlineData("return Environment.OSVersion.Version == null;")]
    [InlineData("return Environment.OSVersion.Version.Major >= 6;")]
    [InlineData("return Environment.OSVersion.VersionString;")]
    [InlineData("return Environment.OSVersion;")]
    [InlineData("return Environment.OSVersion.Version.ToString();")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new object()) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(\"6.1\")) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(v.Major, 1)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, v.Minor)) >= 0;")]
    [InlineData("var os = Environment.OSVersion; return os.Platform == PlatformID.Win32NT;")]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 10;")]
    public void AnOSVersionReadTheShimCanChangeFires(string body) =>
        Assert.True(Fires(body, OSVersion), body);

    [Theory]
    [InlineData("return (int)Math.Ceiling(d);")]
    [InlineData("return Math.Max(1, (int)Math.Ceiling(d));")]
    [InlineData("return (long)Math.Ceiling(d) + (char)Math.Ceiling(d);")]
    [InlineData("return (int)Up(d);")]
    public void ACeilingConvertedToAnIntegerAtOnceDoesNotFire(string body) =>
        Assert.False(Fires(body, Ceiling), body);

    [Theory]
    [InlineData("return Math.Ceiling(d);")]
    [InlineData("return (float)Math.Ceiling(d);")]
    [InlineData("var c = Math.Ceiling(d); return (int)c;")]
    [InlineData("return (int)Math.Ceiling(d) + Math.Ceiling(d);")]
    [InlineData("return Up(d);")]
    public void ACeilingWhoseSignCanBeSeenFires(string body) =>
        Assert.True(Fires(body, Ceiling), body);

    [Theory]
    [InlineData("var f = GetType().GetField(\"x\", BindingFlags.NonPublic | BindingFlags.Instance); if (f != null) f.SetValue(this, o); return null;")]
    [InlineData("GetType().GetField(\"x\", BindingFlags.Public | BindingFlags.Instance).SetValue(this, o); return null;")]
    [InlineData("foreach (var f in GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)) f.SetValue(this, o); return null;")]
    [InlineData("foreach (var f in GetType().GetFields(BindingFlags.Instance)) { var g = f; g.SetValue(this, o); } return null;")]
    public void ASetValueOnAnInstanceFieldDoesNotFire(string body) =>
        Assert.False(Fires(body, SetValue), body);

    [Theory]
    [InlineData("GetType().GetField(\"x\", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, o); return null;")]
    [InlineData("GetType().GetField(\"x\", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).SetValue(this, o); return null;")]
    [InlineData("GetType().GetField(\"x\").SetValue(this, o); return null;")]
    [InlineData("GetType().GetField(\"x\", flags).SetValue(this, o); return null;")]
    [InlineData("field.SetValue(this, o); return null;")]
    [InlineData("GetType().GetField(\"x\", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(this, o); return null;")]
    [InlineData("var f = GetType().GetField(\"x\", BindingFlags.NonPublic | BindingFlags.Instance); f = field; f.SetValue(this, o); return null;")]
    [InlineData("foreach (var f in GetType().GetFields(BindingFlags.Static | BindingFlags.NonPublic)) f.SetValue(null, o); return null;")]
    [InlineData("foreach (var f in fields) f.SetValue(this, o); return null;")]
    [InlineData("typeof(C).GetRuntimeField(\"x\").SetValue(this, o); return null;")]
    public void ASetValueOnAFieldThatCanBeStaticFires(string body) =>
        Assert.True(Fires(body, SetValue), body);

    [Theory]
    [InlineData("return Encoding.ASCII.GetString(b);", GetString)]
    [InlineData("return new string(Encoding.ASCII.GetChars(b));", GetChars)]
    [InlineData("return Encoding.ASCII.GetCharCount(b);", GetCharCount)]
    [InlineData("var a = Encoding.ASCII; return a.GetString(b);", GetString)]
    public void DecodingWithASCIIDoesNotFire(string body, string member) =>
        Assert.False(Fires(body, member), body);

    [Theory]
    [InlineData("return Encoding.UTF8.GetString(b);", GetString)]
    [InlineData("return new string(e.GetChars(b));", GetChars)]
    [InlineData("return Encoding.Default.GetCharCount(b);", GetCharCount)]
    [InlineData("var a = Encoding.ASCII; a = e; return a.GetString(b);", GetString)]
    [InlineData("return Encoding.ASCII.GetString(b) + e.GetString(b);", GetString)]
    [InlineData("Encoding a; a = Encoding.ASCII; return a.GetString(b);", GetString)]
    [InlineData("var a = Encoding.ASCII; (a, x) = (e, 1); return a.GetString(b);", GetString)]
    [InlineData("var a = Encoding.ASCII; a = a; return a.GetString(b);", GetString)]
    public void DecodingWithAnotherEncodingFires(string body, string member) =>
        Assert.True(Fires(body, member), body);

    [Theory]
    [InlineData("return Process.Start(new ProcessStartInfo(\"a\") { UseShellExecute = false });")]
    [InlineData("var psi = new ProcessStartInfo(\"a\", \"b\") { RedirectStandardOutput = true, UseShellExecute = true }; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"uname\", \"-s\"); psi.UseShellExecute = false; psi.RedirectStandardOutput = true; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); psi.UseShellExecute = s; try { return Process.Start(psi); } catch { return null; }")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); var other = new ProcessStartInfo(\"b\"); Console.WriteLine(); x = 1; info.UseShellExecute = true; other.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.UseShellExecute = false; return Process.Start(psi);")]
    public void AStartWhoseShellExecuteIsSetDoesNotFire(string body) =>
        Assert.False(Fires(body, Start), body);

    [Theory]
    [InlineData("return Process.Start(\"a\");")]
    [InlineData("return Process.Start(new ProcessStartInfo(\"a\"));")]
    [InlineData("return Process.Start(info);")]
    [InlineData("return Process.Start(new ProcessStartInfo(\"a\") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });")]
    [InlineData("var psi = new ProcessStartInfo(\"a\") { WindowStyle = ProcessWindowStyle.Hidden }; psi.UseShellExecute = false; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); var p = Process.Start(psi); psi.UseShellExecute = false; return p;")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); if (s) psi.UseShellExecute = false; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\") { UseShellExecute = false }; Configure(psi); return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\") { UseShellExecute = false }; psi = info; return Process.Start(psi);")]
    [InlineData("var psi = info; psi.UseShellExecute = false; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\") { UseShellExecute = false }; var copy = psi; copy.WindowStyle = ProcessWindowStyle.Hidden; return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\") { UseShellExecute = false }; var t = new Tuple<ProcessStartInfo>(psi); return Process.Start(psi);")]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); goto L; psi.UseShellExecute = false; L: return Process.Start(psi);")]
    [InlineData("var p = new Process { StartInfo = new ProcessStartInfo(\"a\") { UseShellExecute = false } }; p.Start(); return p;")]
    public void AStartThatCanUseTheDefaultFires(string body) =>
        Assert.True(Fires(body, Start), body);

    /// <summary>
    /// A call in a lambda or a local function, and a method group, are not lowered with <c>M</c>, so only the fingerprint shows
    /// it. A local function can run before the statement that sets <c>UseShellExecute</c>, and a lambda is not followed either.
    /// </summary>
    [Theory]
    [InlineData("Func<double, double> f = Math.Ceiling; return f;", Ceiling)]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); var p = Run(); psi.UseShellExecute = false; return p; Process Run() => Process.Start(psi);", Start)]
    [InlineData("var psi = new ProcessStartInfo(\"a\"); psi.UseShellExecute = false; Func<Process> run = () => Process.Start(psi); return run;", Start)]
    [InlineData("Func<string, Process> start = Process.Start; return start;", Start)]
    [InlineData("Action<object, object> set = field.SetValue; return set;", SetValue)]
    [InlineData("Func<byte[], string> get = Encoding.ASCII.GetString; return get;", GetString)]
    public void ACallInADelegateFires(string body, string member) =>
        Assert.True(Fingerprinted(Compile(body), member), body);

    private static string Source(string body) => $$"""
        using System;
        using System.Diagnostics;
        using System.Reflection;
        using System.Text;
        class C
        {
            int x;
            static double Up(double d) => Math.Ceiling(d);
            static void Configure(ProcessStartInfo info) { }
            object M(double d, byte[] b, Encoding e, ProcessStartInfo info, FieldInfo field, FieldInfo[] fields, BindingFlags flags, Version v, bool s, object o)
            {
                {{body}}
            }
        }
        """;

    /// <summary>Whether the .NET Framework 4.8 to .NET 10 pair flags <paramref name="member"/> in <c>M</c>, in the lowering and in the fingerprint.</summary>
    private static bool Fires(string body, string member)
    {
        (IMethodSymbol method, Compilation compilation) = Compile(body);
        IrProcedure procedure = IrLowerer.Lower(method, compilation, RenameMap.Empty, [], Runtimes.Migration);
        bool lowered = procedure.Blocks.SelectMany(static b => b.Instructions).Any(i => i switch
        {
            IrCall { Callee: { RuntimeChanged: true } callee } => callee.Value.StartsWith(member, StringComparison.Ordinal),
            IrPure { RuntimeSensitive: true } pure => pure.Function.Contains(member, StringComparison.Ordinal),
            _ => false,
        });

        Assert.Equal(lowered, Fingerprinted((method, compilation), member));
        return lowered;
    }

    /// <summary>Whether the fingerprint of <c>M</c> on the .NET Framework 4.8 to .NET 10 pair names <paramref name="member"/> among its runtime rules.</summary>
    private static bool Fingerprinted((IMethodSymbol Method, Compilation Compilation) compiled, string member) =>
        BodyFingerprinter.Bound(compiled.Method, compiled.Compilation, EquivConfig.Default, legacy: false, Runtimes.Migration).Rules
            .Any(rule => rule.StartsWith(member, StringComparison.Ordinal));

    private static (IMethodSymbol Method, Compilation Compilation) Compile(string body)
    {
        Compilation compilation = RoslynTestCompilations.Compile(Source(body));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return (compilation.GetTypeByMetadataName("C")!.GetMembers("M").OfType<IMethodSymbol>().Single(), compilation);
    }
}
