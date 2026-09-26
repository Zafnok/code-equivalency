using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// The reference assemblies a driver compiles against (ADR 0035, ticket M3-032): the installed .NET Framework 4.8
/// targeting pack for the legacy side, and the newest installed <c>Microsoft.NETCore.App.Ref</c> 10.x pack for the modern
/// side. A side with nothing installed has no paths.
/// </summary>
internal sealed record DriverReferences(IReadOnlyList<string> Legacy, IReadOnlyList<string> Modern)
{
    public static DriverReferences Installed() =>
        Find(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), RuntimeEnvironment.GetRuntimeDirectory());

    /// <summary>
    /// The targeting pack under <paramref name="programFilesX86"/>, and the reference pack of the .NET install whose
    /// shared runtime is <paramref name="runtimeDirectory"/> (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>).
    /// </summary>
    public static DriverReferences Find(string programFilesX86, string runtimeDirectory)
    {
        string framework = Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8");
        string packs = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", "packs", "Microsoft.NETCore.App.Ref"));
        string? pack = Directory.Exists(packs)
            ? Directory.EnumerateDirectories(packs, "10.*").MaxBy(static d => Version.TryParse(Path.GetFileName(d), out Version? version) ? version : null)
            : null;
        return new DriverReferences(Framework(framework), pack is null ? [] : Dlls(Path.Combine(pack, "ref", "net10.0"), static _ => true));
    }

    /// <summary>
    /// The targeting pack's assemblies that its <c>RedistList/FrameworkList.xml</c> names. The folder also holds native
    /// DLLs (<c>System.EnterpriseServices.Thunk.dll</c>) that a compilation rejects; its <c>Facades</c> only forward types.
    /// </summary>
    private static List<string> Framework(string directory)
    {
        string list = Path.Combine(directory, "RedistList", "FrameworkList.xml");
        if (!File.Exists(list))
        {
            return [];
        }

        HashSet<string> names = [.. XDocument.Load(list).Root!.Elements("File").Select(static f => (string)f.Attribute("AssemblyName")!)];
        return Dlls(directory, d => names.Contains(Path.GetFileNameWithoutExtension(d)));
    }

    private static List<string> Dlls(string directory, Func<string, bool> include) =>
        Directory.Exists(directory) ? [.. Directory.EnumerateFiles(directory, "*.dll").Where(include).Order(StringComparer.Ordinal)] : [];
}
