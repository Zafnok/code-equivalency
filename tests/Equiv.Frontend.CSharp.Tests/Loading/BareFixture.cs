using System.Globalization;
using System.IO.Compression;
using System.Text;

using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

/// <summary>
/// A temporary directory for the bare loader's tests (M3-029): project files, a fake .NET Framework reference set
/// emitted with Roslyn (a small <c>mscorlib</c>, <c>System</c>, <c>System.Core</c>, and facades), packages as in-memory
/// <c>.nupkg</c> bytes, and an environment that points the reference-assembly cache into the directory.
/// </summary>
internal sealed class BareFixture : IDisposable
{
    public const string Csharp = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

    private const string CoreLibrarySource = """
        namespace System
        {
            public class Object { }
            public abstract class ValueType { }
            public struct Void { }
            public struct Boolean { }
            public struct Char { }
            public struct Byte { }
            public struct Int16 { }
            public struct Int32 { }
            public struct Int64 { }
            public struct IntPtr { }
            public struct UIntPtr { }
            public struct Single { }
            public struct Double { }
            public sealed class String { }
            public abstract class Enum : ValueType { }
            public abstract class Array { }
            public class Attribute { }
            public class Exception { }
            public abstract class Delegate { }
            public abstract class MulticastDelegate : Delegate { }
            public class Type { }
            public interface IDisposable { }
            public struct RuntimeTypeHandle { }
            public struct RuntimeFieldHandle { }
            public struct RuntimeMethodHandle { }
            public enum AttributeTargets { All = 32767 }
            public sealed class AttributeUsageAttribute : Attribute
            {
                public AttributeUsageAttribute(AttributeTargets validOn) { }
                public bool AllowMultiple { get; set; }
                public bool Inherited { get; set; }
            }
        }
        namespace System.Reflection
        {
            public sealed class AssemblyTitleAttribute : System.Attribute { public AssemblyTitleAttribute(string title) { } }
        }
        namespace System.Runtime.Versioning
        {
            public sealed class TargetFrameworkAttribute : System.Attribute
            {
                public TargetFrameworkAttribute(string frameworkName) { }
                public string FrameworkDisplayName { get; set; }
            }
        }
        """;

    private static readonly Lazy<byte[]> CoreLibrary = new(() => Emit("mscorlib", CoreLibrarySource));

    public BareFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "equiv-M3-029-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Variables = new(StringComparer.Ordinal)
        {
            [ReferenceAssemblyCache.RootVariable] = ReferenceAssemblies,
            ["DOTNET_ROOT"] = Path.Combine(Root, "no-dotnet"),
        };
    }

    public string Root { get; }

    public string ReferenceAssemblies => Path.Combine(Root, "refasm");

    public Dictionary<string, string> Variables { get; }

    public static MetadataReference CoreLibraryReference => MetadataReference.CreateFromImage(CoreLibrary.Value);

    public string? Variable(string name) => Variables.TryGetValue(name, out string? value) ? value : null;

    /// <summary>A fake framework at <c>refasm/.NETFramework/&lt;version&gt;</c>, with facades that depend on nothing.</summary>
    public string Framework(string version = "v4.8")
    {
        string directory = Path.Combine(ReferenceAssemblies, ".NETFramework", version);
        WriteBytes(Path.Combine(directory, "mscorlib.dll"), CoreLibrary.Value);
        WriteBytes(Path.Combine(directory, "System.dll"), Emit("System", "namespace System { public class Uri { } }", CoreLibraryReference));
        WriteBytes(Path.Combine(directory, "System.Core.dll"), Emit("System.Core", "namespace System.Linq { public static class Enumerable { } }", CoreLibraryReference));
        WriteBytes(Path.Combine(directory, "System.Numerics.dll"), Emit("System.Numerics", "namespace System.Numerics { public struct BigInteger { } }", CoreLibraryReference));
        WriteBytes(Path.Combine(directory, "Facades", "System.Runtime.dll"), Emit("System.Runtime", "public class RuntimeType { }", CoreLibraryReference));
        WriteBytes(Path.Combine(directory, "Facades", "netstandard.dll"), Emit("netstandard", "public class StandardType { }", CoreLibraryReference));
        Write(Path.Combine(directory, "RedistList", "FrameworkList.xml"), $"""<FileList Name=".NET Framework {version[1..]}" />""");
        return directory;
    }

    /// <summary>Writes <paramref name="content"/> to a path under <see cref="Root"/> (or an absolute one) and returns the full path.</summary>
    public string Write(string path, string content)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string WriteBytes(string path, byte[] content)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    /// <summary>A library assembly written to <paramref name="path"/>, compiled against the fake corlib and <paramref name="references"/>.</summary>
    public string Library(string path, string name, string source, params string[] references) =>
        WriteBytes(path, Emit(name, source, [CoreLibraryReference, .. references.Select(static r => MetadataReference.CreateFromFile(r))]));

    public static byte[] Emit(string name, string source, params MetadataReference[] references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        return result.Success
            ? stream.ToArray()
            : throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
    }

    /// <summary>An old-style C# project: the Common.props and CSharp.targets tool-path imports around <paramref name="body"/>.</summary>
    public static string Project(string body, string frameworkVersion = "v4.8") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
          <PropertyGroup>
            <TargetFrameworkVersion>{frameworkVersion}</TargetFrameworkVersion>
          </PropertyGroup>
        {body}
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
        </Project>
        """;

    /// <summary>A <c>.sln</c> listing <paramref name="projects"/> (paths relative to it), all built.</summary>
    public static string Solution(params string[] projects)
    {
        StringBuilder text = new("Microsoft Visual Studio Solution File, Format Version 12.00\r\n");
        StringBuilder configurations = new();
        foreach (string project in projects)
        {
            string guid = Guid.NewGuid().ToString("D").ToUpperInvariant();
            text.Append(CultureInfo.InvariantCulture, $"Project(\"{{{Csharp}}}\") = \"{Path.GetFileNameWithoutExtension(project)}\", \"{project}\", \"{{{guid}}}\"\r\nEndProject\r\n");
            configurations.Append(CultureInfo.InvariantCulture, $"\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n\t\t{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU\r\n");
        }

        return text
            .Append("Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n\t\tDebug|Any CPU = Debug|Any CPU\r\n\tEndGlobalSection\r\n")
            .Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n").Append(configurations).Append("\tEndGlobalSection\r\nEndGlobal\r\n")
            .ToString();
    }

    /// <summary>A <c>.nupkg</c>: the given files plus the OPC parts a real package carries.</summary>
    public static byte[] Package(params (string Path, byte[] Content)[] files)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string path, byte[] content) in files.Append(("[Content_Types].xml", "<Types />"u8.ToArray())).Append(("_rels/.rels", "<Relationships />"u8.ToArray())))
            {
                using Stream entry = zip.CreateEntry(path).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The package <c>Microsoft.NETFramework.ReferenceAssemblies.&lt;tfm&gt;</c> for <paramref name="version"/>, holding the fake corlib.</summary>
    public static byte[] ReferenceAssemblyPackage(string version) =>
        Package(($"build/.NETFramework/{version}/mscorlib.dll", CoreLibrary.Value));

    /// <summary>A loader whose SDK-style side must not be reached, whose environment is <see cref="Variables"/>, and whose network is <paramref name="get"/> (none by default).</summary>
    public CompositeSolutionLoader Loader(ISolutionLoader? sdkStyle = null, Func<Uri, CancellationToken, Task<byte[]?>>? get = null) =>
        new(sdkStyle ?? new UnreachableLoader(), Variable, get ?? NoNetwork);

    public static Task<byte[]?> NoNetwork(Uri uri, CancellationToken ct) => Task.FromResult<byte[]?>(null);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file still open elsewhere: leave the temporary folder behind.
        }
    }

    private sealed class UnreachableLoader : ISolutionLoader
    {
        public Task<LoadedSolution> LoadAsync(string solutionPath, CancellationToken ct) =>
            throw new InvalidOperationException($"the SDK-style loader was asked to open '{solutionPath}'");
    }
}
