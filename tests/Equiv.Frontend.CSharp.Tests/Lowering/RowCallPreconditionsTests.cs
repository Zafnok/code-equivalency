using Equiv.Core.Configuration;
using Equiv.Core.Ir;
using Equiv.Frontend.CSharp.Fingerprinting;
using Equiv.Frontend.CSharp.Lowering;

using Microsoft.CodeAnalysis;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Lowering;

/// <summary>
/// Tickets P2-190 and P2-216: rows that fire only in a method that makes a call to their member that can reach their
/// documented change. Each call that cannot is next to one like it that can, which still fires. Each case is checked in the
/// lowering and in the fingerprint, which must agree.
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
    private const string Utf8GetString = "System.Text.UTF8Encoding::GetString(";
    private const string StartInfo = "System.Diagnostics.Process::get_StartInfo(";
    private const string ImageIndex = "System.Windows.Forms.TreeNode::set_ImageIndex(";
    private const string SelectedImageIndex = "System.Windows.Forms.TreeNode::set_SelectedImageIndex(";

    [Theory]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT;")]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.CompareTo(new Version(6, 0)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 1)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 2)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(minor: 2, major: 6)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version >= new Version(6, 1);")]
    [InlineData("return new Version(5, 1) < Environment.OSVersion.Version;")]
    [InlineData("return Environment.OSVersion.Version.Major >= 6;")]
    [InlineData("return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6;")]
    [InlineData("return 6 <= Environment.OSVersion.Version.Major;")]
    [InlineData("return Environment.OSVersion.Version.Major < 6;")]
    [InlineData("return Environment.OSVersion.Version.Major > 5;")]
    [InlineData("return Environment.OSVersion.Version.Major <= 4;")]
    [InlineData("return Environment.OSVersion.Version.Major == 5;")]
    [InlineData("return 5 != Environment.OSVersion.Version.Major;")]
    public void AnOSVersionReadTheShimCannotChangeDoesNotFire(string body) =>
        Assert.False(Fires(body, OSVersion), body);

    [Theory]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 3)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version >= new Version(10, 0);")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(new Version(6, 1, 7601)) >= 0;")]
    [InlineData("return Environment.OSVersion.Version.CompareTo(v) >= 0;")]
    [InlineData("return Environment.OSVersion.Version == null;")]
    [InlineData("return Environment.OSVersion.Version.Major >= 7;")]
    [InlineData("return Environment.OSVersion.Version.Major > 6;")]
    [InlineData("return Environment.OSVersion.Version.Major == 6;")]
    [InlineData("return 10 != Environment.OSVersion.Version.Major;")]
    [InlineData("return Environment.OSVersion.Version.Major >= int.MaxValue;")]
    [InlineData("return Environment.OSVersion.Version.Major >= v.Major;")]
    [InlineData("return Environment.OSVersion.Version.Major >= 6L;")]
    [InlineData("return Environment.OSVersion.Version.Major;")]
    [InlineData("return Environment.OSVersion.Version.Major + 1 > 6;")]
    [InlineData("return Environment.OSVersion.Version.Minor >= 2;")]
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

    /// <summary>Ticket P2-216: constant bytes that form well-formed UTF-8 decode the same on every runtime.</summary>
    [Theory]
    [InlineData("var a = new byte[3]; a[0] = 0xE3; a[1] = 0x82; a[2] = 0xA2; return Encoding.UTF8.GetString(a);", GetString)]
    [InlineData("var a = new byte[4]; a[0] = 0xE3; a[1] = 0x82; a[2] = 0xA2; return new string(e.GetChars(a));", GetChars)]
    [InlineData("return Encoding.UTF8.GetCharCount(new byte[] { 0x41, 0xC3, 0xA9 });", GetCharCount)]
    [InlineData("var a = new byte[] { 0xE3, 0x82, 0xA2 }; var r = e.GetString(a); Array.Clear(a, 0, 1); a[0] = 0xFF; return r;", GetString)]
    [InlineData("var a = new byte[] { 0xE3, 0x82, 0xA2, 0xE3 }; return e.GetCharCount(a, 0, 3);", GetCharCount)]
    [InlineData("var a = new byte[2]; a[0] = 0xFF; a[0] = 0xC3; if (s) { a[1] = 0xA9; return Encoding.UTF8.GetString(a); } return null;", GetString)]
    [InlineData("var a = new byte[2]; a[0] = 0xC3; a[1] = 0xA9; return new UTF8Encoding().GetString(a, 0, 2);", Utf8GetString)]
    [InlineData("var a = new byte[0]; return Encoding.UTF8.GetString(count: 0, index: 0, bytes: a);", GetString)]
    [InlineData("var a = new byte[1]; switch (x) { case 0: return Encoding.UTF8.GetString(a); } return null;", GetString)]
    public void DecodingConstantWellFormedBytesDoesNotFire(string body, string member) =>
        Assert.False(Fires(body, member), body);

    /// <summary>
    /// Ticket P2-216: bytes that are ill-formed, or that the method does not show are constants when the call reads them. A
    /// use of the array in the statement that makes the call, or before it, is not followed.
    /// </summary>
    [Theory]
    [InlineData("var a = new byte[2]; a[0] = 0xE3; a[1] = 0x82; return Encoding.UTF8.GetString(a);")]
    [InlineData("return Encoding.UTF8.GetString(new byte[] { 0xC0, 0x80 });")]
    [InlineData("return Encoding.UTF8.GetString(new byte[] { 0xED, 0xA0, 0x80 });")]
    [InlineData("var a = new byte[] { 0xE3, 0x82, 0xA2 }; return Encoding.UTF8.GetString(a, 1, 2);")]
    [InlineData("var a = new byte[] { 0xE3, 0x82, 0xA2 }; return Encoding.UTF8.GetString(a, 0, x);")]
    [InlineData("var a = new byte[] { 0xE3, 0x82, 0xA2 }; return Encoding.UTF8.GetString(a, x, 1);")]
    [InlineData("var a = new byte[] { 0x41 }; return Encoding.UTF8.GetString(a, 1, 1);")]
    [InlineData("var a = new byte[] { 0x41 }; return Encoding.UTF8.GetString(a, 2, 0);")]
    [InlineData("var a = new byte[1]; a[0] = b[0]; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; a[x] = 0x41; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; a[1] = 0x41; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; a[0] += 0x41; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; if (s) a[0] = 0xFF; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; var c = a; c[0] = 0xFF; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; a = b; return Encoding.UTF8.GetString(a);")]
    [InlineData("byte[] a; a = new byte[1]; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[x]; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[] { 0x41, (byte)x }; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[5000]; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = b; return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new byte[1]; string r = null; for (var i = 0; i < 2; i++) { r = Encoding.UTF8.GetString(a); a[0] = 0xFF; } return r;")]
    [InlineData("var a = new byte[1]; string r = null; L: r = Encoding.UTF8.GetString(a); a[0] = 0xFF; if (s) goto L; return r;")]
    [InlineData("var a = new byte[1]; Spoil(); return Encoding.UTF8.GetString(a); void Spoil() => a[0] = 0xFF;")]
    [InlineData("var a = new byte[1]; var r = Encoding.UTF8.GetString(a); Action spoil = () => a[0] = 0xFF; return r;")]
    [InlineData("var a = new byte[1]; return Encoding.UTF8.GetString(a) + a.Length;")]
    [InlineData("var a = new byte[1]; if (a.Length > 0) { return Encoding.UTF8.GetString(a); } return null;")]
    [InlineData("var a = new byte[1]; { a[0] = 0xFF; } return Encoding.UTF8.GetString(a);")]
    [InlineData("var a = new[] { new byte[1] }; return Encoding.UTF8.GetString(a[0]);")]
    [InlineData("for (var a = new byte[1]; s;) { return Encoding.UTF8.GetString(a); } return null;")]
    [InlineData("return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(new byte[] { 0x41 }));")]
    public void DecodingBytesThatCanBeIllFormedFires(string body) =>
        Assert.True(Fires(body, GetString), body);

    [Theory]
    [InlineData("return Encoding.UTF8.GetString(b);", GetString)]
    [InlineData("return new string(e.GetChars(b));", GetChars)]
    [InlineData("return Encoding.Default.GetCharCount(b);", GetCharCount)]
    [InlineData("var a = Encoding.ASCII; a = e; return a.GetString(b);", GetString)]
    [InlineData("return Encoding.ASCII.GetString(b) + e.GetString(b);", GetString)]
    [InlineData("Encoding a; a = Encoding.ASCII; return a.GetString(b);", GetString)]
    [InlineData("var a = Encoding.ASCII; (a, x) = (e, 1); return a.GetString(b);", GetString)]
    [InlineData("var a = Encoding.ASCII; a = a; return a.GetString(b);", GetString)]
    [InlineData("return new UTF8Encoding().GetString(b, 0, 1);", Utf8GetString)]
    [InlineData("return Encoding.UTF8.GetChars(new byte[] { 0x41 }, 0, 1, new char[1], 0);", GetChars)]
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

    /// <summary>Ticket P2-216: a process the method creates with <c>new</c> always has a <c>StartInfo</c> to read.</summary>
    [Theory]
    [InlineData("var p = new Process { StartInfo = { FileName = \"a\" } }; return p;")]
    [InlineData("var p = new Process { EnableRaisingEvents = false, StartInfo = { FileName = \"a\" } }; return p.StartInfo.FileName;")]
    [InlineData("var p = new Process(); p.StartInfo.FileName = \"a\"; return p.StartInfo;")]
    [InlineData("return new Process().StartInfo;")]
    [InlineData("using (var p = new Process()) { return p.StartInfo.Arguments; }")]
    public void AStartInfoOfANewProcessDoesNotFire(string body) =>
        Assert.False(Fires(body, StartInfo), body);

    [Theory]
    [InlineData("return process.StartInfo;")]
    [InlineData("return Process.GetProcessById(1).StartInfo;")]
    [InlineData("var p = new Process(); p = process; return p.StartInfo;")]
    [InlineData("return new Process { StartInfo = process.StartInfo };")]
    [InlineData("return new Holder { Child = { StartInfo = { FileName = \"a\" } } };")]
    [InlineData("return new Process { StartInfo = { FileName = process.StartInfo.FileName } };")]
    [InlineData("return new { StartInfo = process.StartInfo };")]
    public void AStartInfoOfAProcessThisCodeMayNotHaveStartedFires(string body) =>
        Assert.True(Fires(body, StartInfo), body);

    /// <summary>A process's own <c>StartInfo</c> is no new process's, even in an initializer of one.</summary>
    [Fact]
    public void AStartInfoOfThisProcessFires() =>
        Assert.True(Fires("return null;", StartInfo, "Started", "Copy"));

    /// <summary>Ticket P2-216: an image index the method shows is at least -2, below which alone .NET throws.</summary>
    [Theory]
    [InlineData("node.ImageIndex = 3; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = -2; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = node.SelectedImageIndex = list.Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = node.SelectedImageIndex = list.Count - 1; return null;", SelectedImageIndex)]
    [InlineData("node.SelectedImageIndex = bytes.Length - 2; return null;", SelectedImageIndex)]
    [InlineData("node.SelectedImageIndex = text.Length; return null;", SelectedImageIndex)]
    [InlineData("node.ImageIndex = list.Count; return null;", ImageIndex)]
    [InlineData("var i = list.Count - 1; node.ImageIndex = i; return null;", ImageIndex)]
    [InlineData("return new TreeNode { ImageIndex = 0 };", ImageIndex)]
    [InlineData("return node.ImageIndex;", ImageIndex)]
    [InlineData("x = node.ImageIndex; x += node.SelectedImageIndex; return null;", ImageIndex)]
    [InlineData("x = node.ImageIndex; x += node.SelectedImageIndex; return null;", SelectedImageIndex)]
    public void AnImageIndexOfAtLeastNoneDoesNotFire(string body, string member) =>
        Assert.False(Fires(body, member), body);

    [Theory]
    [InlineData("node.ImageIndex = x; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = -3; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = list.Count - 3; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = list.Count - x; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = x - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = list.Count + 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = mine.Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = counter.Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = bag.Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = overriding.Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = ((ICollection<int>)list).Count - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = new StringBuilder().Length - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = list.Capacity - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = node.SelectedImageIndex - 1; return null;", ImageIndex)]
    [InlineData("node.ImageIndex++; return null;", ImageIndex)]
    [InlineData("node.ImageIndex += 1; return null;", ImageIndex)]
    [InlineData("var i = 0; i = x; node.ImageIndex = i; return null;", ImageIndex)]
    [InlineData("var i = list.Count - 1; i--; node.ImageIndex = i; return null;", ImageIndex)]
    [InlineData("var i = list.Count - 1; --i; node.ImageIndex = i; return null;", ImageIndex)]
    [InlineData("node.ImageIndex = node.SelectedImageIndex = x; return null;", SelectedImageIndex)]
    public void AnImageIndexThatCanBeBelowNoneFires(string body, string member) =>
        Assert.True(Fires(body, member), body);

    /// <summary>
    /// A deconstruction into a property stays opaque in the lowering (<c>DeconstructionAssignment</c>), so only the fingerprint
    /// shows the setter it runs; the value it sets is not followed.
    /// </summary>
    [Fact]
    public void ADeconstructionIntoAnImageIndexFires() =>
        Assert.True(Fingerprinted(Compile("(node.ImageIndex, x) = (x, 1); return null;"), ImageIndex));

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
    [InlineData("var a = new byte[1]; Func<string> f = () => Encoding.UTF8.GetString(a); return f;", GetString)]
    [InlineData("var a = new byte[1]; return F(); string F() => Encoding.UTF8.GetString(a);", GetString)]
    public void ACallInADelegateFires(string body, string member) =>
        Assert.True(Fingerprinted(Compile(body), member), body);

    private static string Source(string body) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Reflection;
        using System.Text;
        using System.Windows.Forms;
        namespace System.Windows.Forms
        {
            public class TreeNode
            {
                public int ImageIndex { get => 0; set { } }
                public int SelectedImageIndex { get => 0; set { } }
            }
        }
        namespace System.Fake
        {
            public class Counter
            {
                public int Count => -5;
            }
            public abstract class Bag : IReadOnlyCollection<int>
            {
                public virtual int Count => 0;
                public IEnumerator<int> GetEnumerator() => null;
                Collections.IEnumerator Collections.IEnumerable.GetEnumerator() => null;
            }
            public sealed class Overriding : Bag
            {
                public override int Count => -5;
            }
        }
        class Started : Process
        {
            object Copy() => new Process { StartInfo = StartInfo };
        }
        class Holder
        {
            public Process Child { get; } = new Process();
        }
        class Mine
        {
            public int Count => -5;
        }
        class C
        {
            int x;
            static double Up(double d) => Math.Ceiling(d);
            static void Configure(ProcessStartInfo info) { }
            object M(double d, byte[] b, Encoding e, ProcessStartInfo info, FieldInfo field, FieldInfo[] fields, BindingFlags flags, Version v, bool s, object o,
                Process process, TreeNode node, List<int> list, byte[] bytes, string text, Mine mine, System.Fake.Counter counter, System.Fake.Bag bag,
                System.Fake.Overriding overriding)
            {
                {{body}}
            }
        }
        """;

    /// <summary>
    /// Whether the .NET Framework 4.8 to .NET 10 pair flags <paramref name="member"/> in <paramref name="name"/> of
    /// <paramref name="type"/>, <c>C.M</c> unless named, in the lowering and in the fingerprint.
    /// </summary>
    private static bool Fires(string body, string member, string type = "C", string name = "M")
    {
        (IMethodSymbol method, Compilation compilation) = Compile(body, type, name);
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

    private static (IMethodSymbol Method, Compilation Compilation) Compile(string body, string type = "C", string name = "M")
    {
        Compilation compilation = RoslynTestCompilations.Compile(Source(body));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(static d => d.Severity == DiagnosticSeverity.Error));
        return (compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().Single(), compilation);
    }
}
