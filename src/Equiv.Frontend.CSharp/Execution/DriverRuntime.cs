using Equiv.Core;

using Microsoft.CodeAnalysis.CSharp;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// How a driver runs on one detected runtime (ADR 0040 decision 3; ticket P2-056). .NET Framework: an <c>.exe</c> with an
/// <c>app.config</c> whose <c>supportedRuntime</c> sku names <see cref="Target"/>'s version, compiled at C# 7.3. .NET (Core):
/// a <c>.dll</c> with a <c>runtimeconfig.json</c> naming its own <c>net&lt;v&gt;</c> and the installed shared framework
/// <see cref="Version"/>, with <c>rollForward: Disable</c> so no other runtime is used in its place, compiled at the language
/// version that runtime ships with. <see cref="References"/> and <see cref="Desktop"/> are the reference assemblies a
/// member driver compiles against; a replay driver compiles against its project's own and leaves them empty.
/// <see cref="DesktopVersion"/> is the installed <c>Microsoft.WindowsDesktop.App</c>, empty when there is none.
/// </summary>
internal sealed record DriverRuntime(TargetRuntime Target, string Version, string DesktopVersion = "")
{
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>The Windows Desktop reference assemblies (ticket P2-051); the .NET Framework's are in <see cref="References"/>.</summary>
    public IReadOnlyList<string> Desktop { get; init; } = [];

    public bool IsFramework => Target.Family == TargetRuntime.RuntimeFamily.NetFramework;

    public string Extension => IsFramework ? ".exe" : ".dll";

    /// <summary>The C# version the runtime's SDK compiles at by default: 7.3 on .NET Framework and .NET Core 2.x.</summary>
    public LanguageVersion Language => IsFramework ? LanguageVersion.CSharp7_3 : Target.Version.Major switch
    {
        < 3 => LanguageVersion.CSharp7_3,
        3 => LanguageVersion.CSharp8,
        5 => LanguageVersion.CSharp9,
        6 => LanguageVersion.CSharp10,
        7 => LanguageVersion.CSharp11,
        8 => LanguageVersion.CSharp12,
        9 => LanguageVersion.CSharp13,
        10 => LanguageVersion.CSharp14,
        _ => LanguageVersion.Latest,
    };

    /// <summary>Writes the driver's configuration beside <paramref name="driver"/>; <paramref name="desktop"/> runs it on <c>Microsoft.WindowsDesktop.App</c>.</summary>
    public void WriteConfig(string driver, bool desktop)
    {
        if (IsFramework)
        {
            File.WriteAllText(driver + ".config", AppConfig());
        }
        else
        {
            File.WriteAllText(Path.ChangeExtension(driver, ".runtimeconfig.json"), RuntimeConfig(desktop));
        }
    }

    internal string AppConfig() => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <startup>
            <supportedRuntime version="v4.0" sku=".NETFramework,Version=v{Target.Version.ToString()}" />
          </startup>
        </configuration>
        """;

    internal string RuntimeConfig(bool desktop) => $$"""
        {
          "runtimeOptions": {
            "tfm": "{{Target}}{{(desktop ? "-windows" : string.Empty)}}",
            "rollForward": "Disable",
            "framework": { "name": "{{(desktop ? "Microsoft.WindowsDesktop.App" : "Microsoft.NETCore.App")}}", "version": "{{(desktop ? DesktopVersion : Version)}}" }
          }
        }
        """;
}
