using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

using Equiv.Core;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Where a driver's runtime and reference assemblies are installed, for any detected runtime (ADR 0040 decision 3; tickets
/// M3-032, P2-051, P2-056). .NET Framework 4.x: the CLR under <see cref="FrameworkRuntime"/>, and the targeting pack
/// <c>v&lt;version&gt;</c> under <see cref="FrameworkPacks"/>, which holds <c>System.Windows.Forms</c> and
/// <c>System.Drawing</c> too. .NET (Core): the newest installed <c>shared/Microsoft.NETCore.App/&lt;major.minor&gt;.*</c>
/// under <see cref="Dotnet"/>, the newest <c>packs/Microsoft.NETCore.App.Ref/&lt;major.minor&gt;.*</c>, and their Windows
/// Desktop counterparts when both are installed. A runtime that is not installed is never replaced by another one.
/// </summary>
internal sealed record DriverReferences(string FrameworkPacks, string FrameworkRuntime, string Dotnet)
{
    private const string CoreApp = "Microsoft.NETCore.App";
    private const string DesktopApp = "Microsoft.WindowsDesktop.App";

    public static DriverReferences Installed() =>
        Find(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), RuntimeEnvironment.GetRuntimeDirectory(), Environment.GetFolderPath(Environment.SpecialFolder.Windows));

    /// <summary>
    /// The targeting packs under <paramref name="programFilesX86"/>, the .NET Framework CLR under <paramref name="windows"/>,
    /// and the .NET install whose shared runtime is <paramref name="runtimeDirectory"/>
    /// (<c>&lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;</c>).
    /// </summary>
    public static DriverReferences Find(string programFilesX86, string runtimeDirectory, string windows) => new(
        Path.Combine(programFilesX86, "Reference Assemblies", "Microsoft", "Framework", ".NETFramework"),
        Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319"),
        Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..")));

    /// <summary>The installed runtime a driver for <paramref name="target"/> runs on, without reference assemblies; null when it is not installed.</summary>
    public DriverRuntime? Host(TargetRuntime target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Family == TargetRuntime.RuntimeFamily.NetFramework)
        {
            return Directory.Exists(FrameworkRuntime) ? new DriverRuntime(target, string.Empty) : null;
        }

        string shared = Path.Combine(Dotnet, "shared");
        return Newest(Path.Combine(shared, CoreApp), target) is { } core
            ? new DriverRuntime(target, Path.GetFileName(core), Newest(Path.Combine(shared, DesktopApp), target) is { } desktop ? Path.GetFileName(desktop) : string.Empty)
            : null;
    }

    /// <summary>
    /// The installed runtime for <paramref name="target"/> with the reference assemblies a member driver compiles against;
    /// null when the runtime or its reference pack is not installed.
    /// </summary>
    public DriverRuntime? For(TargetRuntime target)
    {
        if (Host(target) is not { } host)
        {
            return null;
        }

        string packs = Path.Combine(Dotnet, "packs");
        List<string> references = host.IsFramework
            ? Framework(Path.Combine(FrameworkPacks, "v" + target.Version.ToString()))
            : Pack(packs, CoreApp + ".Ref", target);
        List<string> desktop = host.DesktopVersion.Length > 0 ? Pack(packs, DesktopApp + ".Ref", target) : [];
        return references.Count == 0 ? null : host with { References = references, Desktop = desktop };
    }

    /// <summary>The newest numbered <c>&lt;major.minor&gt;.*</c> folder under <paramref name="versions"/>, or null.</summary>
    private static string? Newest(string versions, TargetRuntime target) =>
        Directory.Exists(versions)
            ? Directory.EnumerateDirectories(versions, string.Create(CultureInfo.InvariantCulture, $"{target.Version.Major}.{target.Version.Minor}.*"))
                .Where(static d => Version.TryParse(Path.GetFileName(d), out _))
                .MaxBy(static d => Version.Parse(Path.GetFileName(d)))
            : null;

    /// <summary>The <c>ref/&lt;tfm&gt;</c> reference assemblies of the newest installed version of the pack <paramref name="name"/>.</summary>
    private static List<string> Pack(string packs, string name, TargetRuntime target) =>
        Newest(Path.Combine(packs, name), target) is { } pack ? Dlls(Path.Combine(pack, "ref", target.ToString()), static _ => true) : [];

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
