using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// The bare loader's MSBuild evaluation of a non-SDK project (M3-029, ADR 0031). Properties are read in import order
/// under the global properties Roslyn's build host sets, which the project cannot override; items are read after the
/// last property, as MSBuild does. Relative imports that exist are followed. MSBuild's own tool-path imports are not:
/// <c>Microsoft.Common.props</c> stands for <c>Directory.Build.props</c> and the restore-generated
/// <c>obj/*.props</c>, <c>Microsoft.CSharp.targets</c> for the <c>.user</c> file, <c>Directory.Build.targets</c>, the
/// generated <c>obj/*.targets</c> and the defaults the loader applies when it reads a property, and any other tool-path
/// import for nothing. What cannot be evaluated exactly throws <see cref="UnsupportedConstructException"/> (ADR 0029):
/// <c>&lt;Choose&gt;</c>, a <c>&lt;Target&gt;</c> that creates <c>Compile</c>, <c>Reference</c> or
/// <c>ProjectReference</c> items, <c>&lt;COMReference&gt;</c>, a missing relative import not conditioned on
/// <c>Exists</c>, and a condition or property function that something the loader reads depends on.
/// </summary>
internal sealed class MsBuildEvaluator
{
    /// <summary>The value MSBuild gives the <c>Solution*</c> properties when a project is evaluated on its own.</summary>
    public const string Undefined = "*Undefined*";

    private static readonly FrozenSet<string> ReadItemTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "Compile", "Reference", "ProjectReference", "PackageReference", "COMReference");

    private static readonly FrozenSet<string> CreatedItemTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "Compile", "Reference", "ProjectReference");

    private static readonly FrozenSet<string> PathItemTypes = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "Compile", "ProjectReference");

    private static readonly FrozenSet<string> ReadMetadata = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "HintPath", "Aliases", "EmbedInteropTypes", "ReferenceOutputAssembly");

    private static readonly string[] ToolPathImports = ["$(MSBuildToolsPath)", "$(MSBuildBinPath)", "$(MSBuildExtensionsPath)", "$(MSBuildExtensionsPath32)", "$(MSBuildExtensionsPath64)", "$(VSToolsPath)"];

    private static readonly string[] SolutionProperties = ["SolutionDir", "SolutionExt", "SolutionFileName", "SolutionName", "SolutionPath"];

    private readonly string _projectPath;
    private readonly string _projectDirectory;
    private readonly MsBuildProperties _properties;
    private readonly List<(XElement Group, string Directory)> _itemGroups = [];
    private readonly HashSet<string> _imported = new(StringComparer.OrdinalIgnoreCase);

    private MsBuildEvaluator(string projectPath, Func<string, string?> environment)
    {
        _projectPath = projectPath;
        _projectDirectory = Path.GetDirectoryName(projectPath)!;
        _properties = new MsBuildProperties(GlobalProperties(projectPath), environment);
    }

    /// <exception cref="UnsupportedConstructException">The project uses MSBuild the bare loader cannot evaluate exactly.</exception>
    /// <exception cref="System.Xml.XmlException">The project or an import it follows is not well-formed XML.</exception>
    public static EvaluatedProject Evaluate(string projectPath, Func<string, string?> environment)
    {
        MsBuildEvaluator evaluator = new(projectPath, environment);
        evaluator.Process(projectPath);
        return new EvaluatedProject(projectPath, evaluator._properties, evaluator.Items());
    }

    /// <summary>
    /// What Roslyn's build host passes (<c>Configuration=Debug</c>, <c>Platform=AnyCPU</c> and its design-time
    /// switches, M2-001), MSBuild's reserved project properties, and the Windows values of <c>OS</c> and
    /// <c>MSBuildRuntimeType</c>: the bare loader evaluates a project the way the Windows loader does.
    /// </summary>
    private static Dictionary<string, string> GlobalProperties(string projectPath) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Configuration"] = "Debug",
        ["Platform"] = "AnyCPU",
        ["DesignTimeBuild"] = "true",
        ["BuildingInsideVisualStudio"] = "true",
        ["BuildProjectReferences"] = "false",
        ["BuildingProject"] = "false",
        ["ProvideCommandLineArgs"] = "true",
        ["SkipCompilerExecution"] = "true",
        ["ContinueOnError"] = "ErrorAndContinue",
        ["ShouldUnsetParentConfigurationAndPlatform"] = "false",
        ["OS"] = "Windows_NT",
        ["MSBuildRuntimeType"] = "Full",
        ["MSBuildProjectFullPath"] = projectPath,
        ["MSBuildProjectDirectory"] = Path.GetDirectoryName(projectPath)!,
        ["MSBuildProjectFile"] = Path.GetFileName(projectPath),
        ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(projectPath),
        ["MSBuildProjectExtension"] = Path.GetExtension(projectPath),
    };

    private void Process(string file)
    {
        // MSBuild imports a file once and warns about the rest.
        if (!_imported.Add(file))
        {
            return;
        }

        XElement root = XDocument.Load(file).Root!;
        string directory = Path.GetDirectoryName(file)!;
        string[] thisFile = ["MSBuildThisFile", "MSBuildThisFileDirectory", "MSBuildThisFileFullPath", "MSBuildThisFileName"];
        PropertyValue[] saved = [.. thisFile.Select(n => _properties[n])];
        _properties.Set("MSBuildThisFile", new PropertyValue(Path.GetFileName(file)));
        _properties.Set("MSBuildThisFileDirectory", new PropertyValue(directory + Path.DirectorySeparatorChar));
        _properties.Set("MSBuildThisFileFullPath", new PropertyValue(file));
        _properties.Set("MSBuildThisFileName", new PropertyValue(Path.GetFileNameWithoutExtension(file)));
        foreach (XElement element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "PropertyGroup":
                    PropertyGroup(element, directory);
                    break;
                case "ItemGroup":
                    _itemGroups.Add((element, directory));
                    break;
                case "Import":
                    Import(element, directory);
                    break;
                case "ImportGroup" when MsBuildCondition.Evaluate(Condition(element), _properties, directory):
                    foreach (XElement import in element.Elements().Where(static e => string.Equals(e.Name.LocalName, "Import", StringComparison.Ordinal)))
                    {
                        Import(import, directory);
                    }

                    break;
                case "Choose":
                    throw new UnsupportedConstructException("<Choose>");
                case "Target":
                    CheckTarget(element);
                    break;
            }
        }

        for (int i = 0; i < thisFile.Length; i++)
        {
            _properties.Set(thisFile[i], saved[i]);
        }
    }

    /// <summary>
    /// A property group, in document order. A property whose condition (or whose group's condition) cannot be evaluated
    /// is not set to a guess: it takes a value that names the condition, and fails only if the loader reads it.
    /// </summary>
    private void PropertyGroup(XElement group, string directory)
    {
        (bool holds, string? groupUnsupported) = Test(group, directory);
        if (!holds)
        {
            return;
        }

        foreach (XElement property in group.Elements())
        {
            (bool set, string? unsupported) = groupUnsupported is null ? Test(property, directory) : (true, groupUnsupported);
            if (set)
            {
                _properties.Set(property.Name.LocalName, unsupported is null ? _properties.Expand(property.Value.Trim()) : PropertyValue.Poisoned(unsupported));
            }
        }
    }

    private (bool Holds, string? Unsupported) Test(XElement element, string directory)
    {
        try
        {
            return (MsBuildCondition.Evaluate(Condition(element), _properties, directory), null);
        }
        catch (UnsupportedConstructException exception)
        {
            return (true, exception.Construct);
        }
    }

    private void Import(XElement import, string directory)
    {
        string raw = import.Attribute("Project")?.Value ?? string.Empty;
        if (ToolPathImports.Any(p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            ToolPathImport(raw);
            return;
        }

        string condition = Condition(import);
        if (!MsBuildCondition.Evaluate(condition, _properties, directory))
        {
            return;
        }

        PropertyValue path = _properties.Expand(raw);
        if (path.ToolPath)
        {
            ToolPathImport(raw);
            return;
        }

        string spec = MsBuildProperties.Unescape(path.Exact);
        if (ProjectPath.IsWildcard(spec))
        {
            foreach (string file in ProjectPath.Glob(directory, spec))
            {
                Process(file);
            }
        }
        else if (ProjectPath.Resolve(directory, spec) is { } file)
        {
            Process(file);
        }
        else if (!condition.Contains("Exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedConstructException($"the missing import '{raw}'");
        }
    }

    /// <summary>The built-in knowledge that replaces an import from MSBuild's own tool path.</summary>
    private void ToolPathImport(string raw)
    {
        string name = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        if (string.Equals(name, "Microsoft.Common.props", StringComparison.OrdinalIgnoreCase))
        {
            CommonProps();
        }
        else if (string.Equals(name, "Microsoft.CSharp.targets", StringComparison.OrdinalIgnoreCase))
        {
            CSharpTargets();
        }
    }

    private void CommonProps()
    {
        if (!_properties.Read("ImportDirectoryBuildProps").Equals("false", StringComparison.OrdinalIgnoreCase)
            && FindAbove("Directory.Build.props") is { } props)
        {
            Process(props);
        }

        _properties.Default("BaseIntermediateOutputPath", @"obj\");
        _properties.Default("MSBuildProjectExtensionsPath", _properties.Read("BaseIntermediateOutputPath"));
        ProjectExtensions("ImportProjectExtensionProps", ".props");
    }

    private void CSharpTargets()
    {
        foreach (string property in SolutionProperties)
        {
            _properties.Default(property, Undefined);
        }

        if (ProjectPath.Resolve(_projectDirectory, Path.GetFileName(_projectPath) + ".user") is { } user)
        {
            Process(user);
        }

        if (!_properties.Read("ImportDirectoryBuildTargets").Equals("false", StringComparison.OrdinalIgnoreCase)
            && FindAbove("Directory.Build.targets") is { } targets)
        {
            Process(targets);
        }

        ProjectExtensions("ImportProjectExtensionTargets", ".targets");
    }

    /// <summary>The files a restore generates next to the assets file (<c>obj/&lt;project file&gt;.nuget.g.props</c> and friends).</summary>
    private void ProjectExtensions(string switchProperty, string extension)
    {
        if (_properties.Read(switchProperty).Equals("false", StringComparison.OrdinalIgnoreCase)
            || ProjectPath.Resolve(_projectDirectory, MsBuildProperties.Unescape(_properties.Read("MSBuildProjectExtensionsPath"))) is not { } directory)
        {
            return;
        }

        foreach (string file in ProjectPath.Glob(directory, $"{Path.GetFileName(_projectPath)}.*{extension}"))
        {
            Process(file);
        }
    }

    /// <summary>The nearest file called <paramref name="name"/> in the project's directory or above it.</summary>
    private string? FindAbove(string name)
    {
        for (DirectoryInfo? directory = new(_projectDirectory); directory is not null; directory = directory.Parent)
        {
            if (ProjectPath.Resolve(directory.FullName, name) is { } file && File.Exists(file))
            {
                return file;
            }
        }

        return null;
    }

    private static void CheckTarget(XElement target)
    {
        foreach (XElement element in target.Descendants())
        {
            string? created = string.Equals(element.Name.LocalName, "Output", StringComparison.Ordinal)
                ? element.Attribute("ItemName")?.Value
                : string.Equals(element.Parent!.Name.LocalName, "ItemGroup", StringComparison.Ordinal) && element.Attribute("Include") is not null ? element.Name.LocalName : null;
            if (created is not null && CreatedItemTypes.Contains(created))
            {
                throw new UnsupportedConstructException($"a <Target> that creates {created} items (target '{target.Attribute("Name")?.Value}')");
            }
        }
    }

    private ImmutableArray<EvaluatedItem> Items()
    {
        List<EvaluatedItem> items = [];
        foreach ((XElement group, string directory) in _itemGroups)
        {
            List<XElement> read = [.. group.Elements().Where(static e => ReadItemTypes.Contains(e.Name.LocalName))];
            if (read.Count == 0 || !MsBuildCondition.Evaluate(Condition(group), _properties, directory))
            {
                continue;
            }

            foreach (XElement element in read.Where(e => MsBuildCondition.Evaluate(Condition(e), _properties, directory)))
            {
                Item(element, directory, items);
            }
        }

        return [.. items];
    }

    private void Item(XElement element, string directory, List<EvaluatedItem> items)
    {
        string type = element.Name.LocalName;
        if (string.Equals(type, "COMReference", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedConstructException("<COMReference>");
        }

        if (element.Attribute("Remove") is { } remove)
        {
            HashSet<string> removed = new(Specs(type, remove.Value), StringComparer.OrdinalIgnoreCase);
            items.RemoveAll(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase) && removed.Contains(i.Include));
            return;
        }

        if (element.Attribute("Include") is not { } include)
        {
            // Update changes metadata only, and none that the loader reads for Compile items.
            return;
        }

        HashSet<string> excluded = element.Attribute("Exclude") is { } exclude ? new(Specs(type, exclude.Value), StringComparer.OrdinalIgnoreCase) : [];
        ImmutableDictionary<string, string> metadata = Metadata(element, directory);
        items.AddRange(Specs(type, include.Value).Where(s => !excluded.Contains(s)).Select(s => new EvaluatedItem(type, s, metadata)));
    }

    /// <summary>An item attribute's specs: full paths (wildcards expanded) for path items, the text itself otherwise.</summary>
    private IEnumerable<string> Specs(string type, string text)
    {
        IEnumerable<string> specs = _properties.Expand(text).Exact
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MsBuildProperties.Unescape);
        return PathItemTypes.Contains(type)
            ? specs.SelectMany(s => ProjectPath.IsWildcard(s) ? ProjectPath.Glob(_projectDirectory, s) : [ProjectPath.Resolve(_projectDirectory, s) ?? ProjectPath.Full(_projectDirectory, s)])
            : specs;
    }

    private ImmutableDictionary<string, string> Metadata(XElement item, string directory)
    {
        ImmutableDictionary<string, string>.Builder metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XAttribute attribute in item.Attributes().Where(static a => ReadMetadata.Contains(a.Name.LocalName)))
        {
            metadata[attribute.Name.LocalName] = MsBuildProperties.Unescape(_properties.Expand(attribute.Value).Exact);
        }

        foreach (XElement child in item.Elements().Where(c => ReadMetadata.Contains(c.Name.LocalName) && MsBuildCondition.Evaluate(Condition(c), _properties, directory)))
        {
            metadata[child.Name.LocalName] = MsBuildProperties.Unescape(_properties.Expand(child.Value.Trim()).Exact);
        }

        return metadata.ToImmutable();
    }

    private static string Condition(XElement element) => element.Attribute("Condition")?.Value ?? string.Empty;
}
