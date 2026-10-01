using Equiv.Core;
using Equiv.Frontend.CSharp.Lowering;

namespace Equiv.Tests.Integration;

/// <summary>The runtime facts tests lower and fingerprint a body with (ADR 0040; ticket P2-055).</summary>
internal static class Runtimes
{
    /// <summary>.NET Framework 4.8 against .NET 10, the pair every migration sample is: it crosses every runtime rule but x87.</summary>
    public static SideRuntime Migration { get; } = Between("net48", "net10.0");

    /// <summary>A side of a pair whose projects run on <paramref name="legacy"/> and <paramref name="modern"/>.</summary>
    public static SideRuntime Between(string legacy, string modern, bool x87 = false) => new(Interval(legacy, modern), x87);

    public static RuntimeInterval Interval(string legacy, string modern) => new(TargetRuntime.Parse(legacy)!, TargetRuntime.Parse(modern)!);
}
