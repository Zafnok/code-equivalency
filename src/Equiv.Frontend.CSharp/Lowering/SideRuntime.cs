using System.Linq;

using Equiv.Core;
using Equiv.Core.RuntimeChanges;
using Equiv.Frontend.CSharp.Loading;

using Microsoft.CodeAnalysis;

namespace Equiv.Frontend.CSharp.Lowering;

/// <summary>
/// The runtime facts one side's body is lowered and fingerprinted with (ADR 0040 decision 2; ticket P2-055).
/// <see cref="Interval"/> is the pair's: the runtimes between the two projects the bodies come from, the same on both sides,
/// so a fragment both sides hold is fingerprinted alike (ADR 0024). A runtime rule applies only where the interval crosses
/// its change point. <see cref="X87"/> is this side's own: its floating point runs on the 32-bit .NET Framework JIT and the
/// other side's does not, which is the one rule that depends on the platform as well as the runtime.
/// </summary>
internal sealed record SideRuntime(RuntimeInterval Interval, bool X87)
{
    /// <summary>
    /// Whether this side's floating point may run on x87 at all, whatever the other side's does (ADR 0053 decision 4;
    /// ticket P1-030): its arithmetic is then not IEEE binary32 and binary64, so its functions carry
    /// <see cref="PureCatalogue.X87Prefix"/> and no backend interprets them. True whenever <see cref="X87"/> is.
    /// </summary>
    public bool OnX87 { get; init; } = X87;

    /// <summary>The runtime whose floating-point to integer conversions saturate, where earlier ones gave a platform's value.</summary>
    private static readonly TargetRuntime Saturation = new(TargetRuntime.RuntimeFamily.NetCore, new Version(9, 0));

    /// <summary>Whether a floating-point to integer conversion behaves differently on the two sides.</summary>
    public bool FloatToIntegerChanged => Interval.Crosses(Saturation);

    /// <summary>
    /// Both sides of a pair whose legacy body is in <paramref name="legacy"/>'s project and whose modern body is in
    /// <paramref name="modern"/>'s. The interval runs between the oldest and the newest runtime either project runs on (a
    /// library hosted on several runtimes has several). Two unhosted projects with the same <c>netstandard</c> target
    /// framework are one runtime; a pair with any other unhosted project crosses <paramref name="table"/>'s whole coverage.
    /// </summary>
    public static (SideRuntime Legacy, SideRuntime Modern) Of(
        ProjectRuntime legacy, Compilation legacyCompilation, ProjectRuntime modern, Compilation modernCompilation, RuntimeChangeTable table)
    {
        RuntimeInterval interval = Between(legacy, modern, table);
        return (
            new SideRuntime(interval, MayBeX87(legacy, legacyCompilation) && !IsX87(modern, modernCompilation)) { OnX87 = MayBeX87(legacy, legacyCompilation) },
            new SideRuntime(interval, MayBeX87(modern, modernCompilation) && !IsX87(legacy, legacyCompilation)) { OnX87 = MayBeX87(modern, modernCompilation) });
    }

    private static RuntimeInterval Between(ProjectRuntime legacy, ProjectRuntime modern, RuntimeChangeTable table)
    {
        TargetRuntime[] known = [.. legacy.Runtimes, .. modern.Runtimes];
        return (legacy.Runtimes.IsEmpty, modern.Runtimes.IsEmpty) switch
        {
            (false, false) => new RuntimeInterval(known.Min()!, known.Max()!),
            (true, true) when IsStandard(legacy.Declared) && string.Equals(legacy.Declared, modern.Declared, StringComparison.Ordinal) =>
                new RuntimeInterval(table.CoveredFrom, table.CoveredFrom),
            _ => table.Coverage,
        };
    }

    private static bool IsStandard(string declared) => declared.StartsWith("netstandard", StringComparison.Ordinal);

    /// <summary>
    /// Whether the project's floating point may run on x87: its platform is 32-bit and some runtime it runs on is .NET
    /// Framework, or is not known. A .NET (Core) project never does.
    /// </summary>
    private static bool MayBeX87(ProjectRuntime project, Compilation compilation) =>
        Is32Bit(compilation) && (project.Runtimes.IsEmpty || project.Runtimes.Any(IsFramework));

    /// <summary>Whether the project's floating point does run on x87: its platform is 32-bit and every runtime it runs on is .NET Framework.</summary>
    private static bool IsX87(ProjectRuntime project, Compilation compilation) =>
        Is32Bit(compilation) && !project.Runtimes.IsEmpty && project.Runtimes.All(IsFramework);

    private static bool IsFramework(TargetRuntime runtime) => runtime.Family == TargetRuntime.RuntimeFamily.NetFramework;

    private static bool Is32Bit(Compilation compilation) => compilation.Options.Platform is Platform.X86 or Platform.AnyCpu32BitPreferred;
}
