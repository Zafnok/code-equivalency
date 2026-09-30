using Equiv.Core;
using Equiv.Frontend.CSharp.Execution;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

public sealed class DriverReferencesTests : IDisposable
{
    private static readonly TargetRuntime Net48 = TargetRuntime.Parse("net48")!;

    private static readonly TargetRuntime Net472 = TargetRuntime.Parse("net472")!;

    private static readonly TargetRuntime Net8 = TargetRuntime.Parse("net8.0")!;

    private static readonly TargetRuntime Net10 = TargetRuntime.Parse("net10.0")!;

    private readonly string root = Directory.CreateTempSubdirectory("driver-references-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Touch(params string[] path)
    {
        string file = Path.Combine([root, .. path]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Empty);
        return file;
    }

    private DriverReferences Find() =>
        DriverReferences.Find(Path.Combine(root, "x86"), Path.Combine(root, "dotnet", "shared", "Microsoft.NETCore.App", "10.0.12"), Path.Combine(root, "windows"));

    private void FrameworkRuntime() => Directory.CreateDirectory(Path.Combine(root, "windows", "Microsoft.NET", "Framework", "v4.0.30319"));

    [Fact]
    public void FindsTheFrameworkListAndTheNewestPackOfEachRuntime()
    {
        FrameworkRuntime();
        string[] framework = ["x86", "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8"];
        File.WriteAllText(
            Path.GetDirectoryName(Touch([.. framework, "RedistList", "FrameworkList.xml"]))! + "/FrameworkList.xml",
            """<FileList><File AssemblyName="mscorlib" /><File AssemblyName="System" /></FileList>""");
        string mscorlib = Touch([.. framework, "mscorlib.dll"]);
        string system = Touch([.. framework, "System.dll"]);
        Touch([.. framework, "System.EnterpriseServices.Thunk.dll"]);
        Touch([.. framework, "Facades", "System.Runtime.dll"]);
        string[] packs = ["dotnet", "packs", "Microsoft.NETCore.App.Ref"];
        Touch([.. packs, "10.0.9", "ref", "net10.0", "Old.dll"]);
        string runtime = Touch([.. packs, "10.0.12", "ref", "net10.0", "System.Runtime.dll"]);
        Touch([.. packs, "10.0.13-preview", "ref", "net10.0", "Preview.dll"]);
        string eight = Touch([.. packs, "8.0.4", "ref", "net8.0", "Eight.dll"]);
        string[] desktop = ["dotnet", "packs", "Microsoft.WindowsDesktop.App.Ref"];
        Touch([.. desktop, "10.0.9", "ref", "net10.0", "Old.dll"]);
        string forms = Touch([.. desktop, "10.0.12", "ref", "net10.0", "System.Windows.Forms.dll"]);
        Touch("dotnet", "shared", "Microsoft.NETCore.App", "10.0.12", "System.Runtime.dll");
        Touch("dotnet", "shared", "Microsoft.NETCore.App", "10.0.3", "System.Runtime.dll");
        Touch("dotnet", "shared", "Microsoft.NETCore.App", "8.0.21", "System.Runtime.dll");
        Touch("dotnet", "shared", "Microsoft.WindowsDesktop.App", "10.0.12", "System.Windows.Forms.dll");

        DriverReferences references = Find();

        DriverRuntime legacy = references.For(Net48)!;
        Assert.Equal([system, mscorlib], legacy.References, StringComparer.Ordinal);
        Assert.Empty(legacy.Desktop);
        DriverRuntime modern = references.For(Net10)!;
        Assert.Equal(("10.0.12", "10.0.12"), (modern.Version, modern.DesktopVersion));
        Assert.Equal([runtime], modern.References, StringComparer.Ordinal);
        Assert.Equal([forms], modern.Desktop, StringComparer.Ordinal);
        DriverRuntime net8 = references.For(Net8)!;
        Assert.Equal(("8.0.21", string.Empty), (net8.Version, net8.DesktopVersion));
        Assert.Equal([eight], net8.References, StringComparer.Ordinal);
        Assert.Empty(net8.Desktop);
    }

    /// <summary>Ticket P2-056 criterion 2: a runtime without its reference pack or its shared runtime has no driver runtime, and no other version stands in.</summary>
    [Fact]
    public void MissingPack_IsNotConstructible()
    {
        FrameworkRuntime();
        Touch("dotnet", "packs", "Microsoft.NETCore.App.Ref", "10.0.1", "empty.txt");
        Touch("dotnet", "packs", "Microsoft.NETCore.App.Ref", "8.0.4", "ref", "net8.0", "Eight.dll");
        Touch("dotnet", "shared", "Microsoft.NETCore.App", "10.0.1", "System.Runtime.dll");
        Touch("dotnet", "shared", "Microsoft.WindowsDesktop.App", "10.0.1", "System.Windows.Forms.dll");
        Touch("x86", "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8", "mscorlib.dll");
        DriverReferences references = Find();
        DriverReferences none = DriverReferences.Find(Path.Combine(root, "missing"), Path.Combine(root, "missing", "a", "b", "c"), Path.Combine(root, "missing"));

        // A framework without a FrameworkList, a 10.0 pack without net10.0 assemblies, net8.0's pack without its runtime.
        Assert.Null(references.For(Net48));
        Assert.Null(references.For(Net472));
        Assert.Null(references.For(Net10));
        Assert.Null(references.For(Net8));
        Assert.NotNull(references.Host(Net10));
        Assert.Null(references.Host(Net8));
        Assert.Null(none.For(Net48));
        Assert.Null(none.For(Net10));
        Assert.Null(none.Host(Net48));
        Assert.Throws<ArgumentNullException>(() => none.Host(null!));
        Assert.Equal("runtime net8.0 not installed", DriverFactory.NotInstalled(Net8));
    }

    [Fact]
    public void TheInstalledReferencesIncludeThisRuntimesPack()
    {
        TargetRuntime self = new(TargetRuntime.RuntimeFamily.NetCore, new Version(Environment.Version.Major, Environment.Version.Minor));

        DriverRuntime installed = DriverReferences.Installed().For(self)!;

        Assert.NotEmpty(installed.References);
        Assert.StartsWith($"{self.Version}.", installed.Version, StringComparison.Ordinal);
    }
}
