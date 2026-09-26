using System.Xml;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// Whether a project file is SDK-style, by the test Roslyn's build-host manager uses to pick the .NET SDK build host
/// (M3-029): a root <c>Sdk</c> attribute, an <c>&lt;Import Sdk=...&gt;</c>, an <c>&lt;Sdk&gt;</c> element, or a
/// <c>TargetFramework</c>/<c>TargetFrameworks</c> property. Every other project is old-style and, off Windows, goes to
/// the bare loader.
/// </summary>
internal static class SdkStyleProject
{
    /// <summary>Whether the project file at <paramref name="path"/> is SDK-style; a missing or malformed file is not.</summary>
    public static bool IsSdkStyleFile(string path)
    {
        try
        {
            return File.Exists(path) && IsSdkStyle(XDocument.Load(path));
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public static bool IsSdkStyle(XDocument project) =>
        project.Root is { } root
        && (root.Attribute("Sdk") is not null
            || root.Descendants().Any(static e => e.Name.LocalName switch
            {
                "Import" => e.Attribute("Sdk") is not null,
                "Sdk" or "TargetFramework" or "TargetFrameworks" => true,
                _ => false,
            }));
}
