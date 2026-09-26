using System.Xml.Linq;

using Equiv.Frontend.CSharp.Loading;

using Xunit;

namespace Equiv.Frontend.CSharp.Tests.Loading;

public sealed class SdkStyleProjectTests
{
    private const string MsBuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

    public static TheoryData<string, string> Signals => new()
    {
        { "root Sdk attribute", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup /></Project>""" },
        { "Import Sdk", """<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" /></Project>""" },
        { "Sdk element", """<Project><Sdk Name="Microsoft.NET.Sdk" /></Project>""" },
        { "TargetFramework", """<Project><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>""" },
        { "TargetFrameworks", """<Project><PropertyGroup Condition="true"><TargetFrameworks>net48;net10.0</TargetFrameworks></PropertyGroup></Project>""" },
    };

    [Theory]
    [MemberData(nameof(Signals))]
    public void SdkStyleDetectionMatchesRoslyn(string signal, string project)
    {
        Assert.True(SdkStyleProject.IsSdkStyle(XDocument.Parse(project)), signal);
    }

    [Fact]
    public void AnOldStyleProjectIsNotSdkStyle()
    {
        XDocument project = XDocument.Parse($"""
            <Project ToolsVersion="15.0" xmlns="{MsBuildNamespace}">
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
              <PropertyGroup><TargetFrameworkVersion>v4.8</TargetFrameworkVersion></PropertyGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);

        Assert.False(SdkStyleProject.IsSdkStyle(project));
    }

    [Fact]
    public void TheSignalsAreMatchedByLocalNameInTheMsBuildNamespace() =>
        Assert.True(SdkStyleProject.IsSdkStyle(XDocument.Parse($"""<Project xmlns="{MsBuildNamespace}"><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>""")));

    [Fact]
    public void AnEmptyDocumentIsNotSdkStyle() => Assert.False(SdkStyleProject.IsSdkStyle(new XDocument()));
}
