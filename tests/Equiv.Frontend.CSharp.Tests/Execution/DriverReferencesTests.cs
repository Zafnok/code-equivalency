using Equiv.Frontend.CSharp.Execution;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Execution;

public sealed class DriverReferencesTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("driver-references-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    private string Touch(params string[] path)
    {
        string file = Path.Combine([root, .. path]);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Empty);
        return file;
    }

    [Fact]
    public void FindsTheFrameworkListAndTheNewestTenPack()
    {
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
        Touch([.. packs, "9.0.1", "ref", "net10.0", "Nine.dll"]);

        DriverReferences references = DriverReferences.Find(Path.Combine(root, "x86"), Path.Combine(root, "dotnet", "shared", "Microsoft.NETCore.App", "10.0.12"));

        Assert.Equal([system, mscorlib], references.Legacy, StringComparer.Ordinal);
        Assert.Equal([runtime], references.Modern, StringComparer.Ordinal);
    }

    [Fact]
    public void NothingInstalledMeansNoReferences()
    {
        Touch("dotnet", "packs", "Microsoft.NETCore.App.Ref", "10.0.1", "empty.txt");
        Touch("x86", "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8", "mscorlib.dll");

        DriverReferences references = DriverReferences.Find(Path.Combine(root, "x86"), Path.Combine(root, "dotnet", "shared", "Microsoft.NETCore.App", "10.0.1"));
        DriverReferences none = DriverReferences.Find(Path.Combine(root, "missing"), Path.Combine(root, "missing", "a", "b", "c"));

        Assert.Empty(references.Legacy);
        Assert.Empty(references.Modern);
        Assert.Empty(none.Legacy);
        Assert.Empty(none.Modern);
    }

    [Fact]
    public void TheInstalledReferencesIncludeThisRuntimesPack() => Assert.NotEmpty(DriverReferences.Installed().Modern);
}
