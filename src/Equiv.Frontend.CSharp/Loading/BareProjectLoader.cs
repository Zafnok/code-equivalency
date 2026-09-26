using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>
/// ADR 0004's bare loader for non-SDK C# projects (M3-029, ADR 0031): evaluates the project XML
/// (<see cref="MsBuildEvaluator"/>), takes its <c>Compile</c> items, resolves its references
/// (<see cref="AssemblyReferences"/>, <see cref="ProjectAssets"/>, <see cref="ReferenceAssemblyCache"/>) and builds the
/// <see cref="CSharpCompilation"/> itself. A <c>ProjectReference</c> to another non-SDK project loads that project too,
/// once; one to an SDK-style project binds against the compilation MSBuildWorkspace built for it. A project that cannot
/// be loaded exactly is returned with a workspace failure and no compilation (ADR 0029), never approximated.
/// </summary>
internal sealed class BareProjectLoader(
    Func<string, string?> environment,
    ReferenceAssemblyCache referenceAssemblies,
    PackageFeed feed,
    string? netStandardShims,
    IReadOnlyDictionary<string, Compilation> sdkProjects)
{
    private readonly Dictionary<string, BareProject?> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<BareProject> _loaded = [];

    /// <summary>Every project loaded so far, in the order each finished, including those only reached by reference.</summary>
    public IReadOnlyList<BareProject> Projects => _loaded;

    public async Task<BareProject> LoadAsync(string projectPath, CancellationToken ct)
    {
        if (_projects.TryGetValue(projectPath, out BareProject? known))
        {
            return known ?? Failed(projectPath, "it is part of a project reference cycle");
        }

        _projects[projectPath] = null;
        BareProject project = await OpenAsync(projectPath, ct).ConfigureAwait(false);
        _projects[projectPath] = project;
        _loaded.Add(project);
        return project;
    }

    private async Task<BareProject> OpenAsync(string projectPath, CancellationToken ct)
    {
        if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(projectPath);
            LoadDiagnostic unsupported = new(LoadDiagnosticKind.UnsupportedProject, string.Empty, name, $"project type '{System.IO.Path.GetExtension(projectPath)}' is not supported; only C# is");
            return new BareProject(name, projectPath, name, IsCSharp: false, Compilation: null, [unsupported]);
        }

        if (!File.Exists(projectPath))
        {
            return Failed(projectPath, $"the project file '{projectPath}' does not exist");
        }

        try
        {
            return await CompileAsync(MsBuildEvaluator.Evaluate(projectPath, environment), ct).ConfigureAwait(false);
        }
        catch (UnsupportedConstructException exception)
        {
            return Failed(projectPath, exception.Message);
        }
        catch (XmlException exception)
        {
            return Failed(projectPath, $"a project file it imports is not well-formed XML: {exception.Message}");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            return Failed(projectPath, $"its project.assets.json cannot be read: {exception.Message}");
        }
    }

    private async Task<BareProject> CompileAsync(EvaluatedProject project, CancellationToken ct)
    {
        MsBuildProperties properties = project.Properties;
        string name = System.IO.Path.GetFileNameWithoutExtension(project.Path);
        string assemblyName = properties.Read("AssemblyName").Trim() is { Length: > 0 } configured ? configured : name;
        (Version version, string versionText, string profile, string moniker) = TargetFramework(properties);
        if (await referenceAssemblies.DirectoryAsync(versionText, feed, ct).ConfigureAwait(false) is not { } root)
        {
            return Failed(project.Path, $"no reference assemblies for {moniker} are cached in '{referenceAssemblies.Root}' or found on {string.Join(", ", feed.Sources)}");
        }

        (ProjectAssets? assets, string assetsFile) = await PackageAssetsAsync(project, moniker, ct).ConfigureAwait(false);
        if (assets is null)
        {
            return Failed(project.Path, $"it has PackageReference items but no '{assetsFile}'; restore it first (dotnet restore)");
        }

        string frameworkDirectory = profile.Length > 0 ? System.IO.Path.Combine(root, "Profile", profile) : root;
        List<MetadataReference> references =
        [
            .. AssemblyReferences.Resolve(project, frameworkDirectory, version, assets, netStandardShims)
                .Select(static r => MetadataReference.CreateFromFile(r.Path, new MetadataReferenceProperties(MetadataImageKind.Assembly, r.Aliases, r.EmbedInteropTypes))),
            .. await ProjectReferencesAsync(project, ct).ConfigureAwait(false),
        ];
        ImmutableArray<string> sources = [.. project.OfType("Compile").Select(static c => c.Include).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (sources.FirstOrDefault(static s => !File.Exists(s)) is { } missing)
        {
            return Failed(project.Path, $"the source file '{missing}' does not exist");
        }

        CSharpParseOptions parseOptions = BareCompilationOptions.Parse(properties);
        List<SyntaxTree> trees = await ParseAsync(sources, parseOptions, ct).ConfigureAwait(false);
        if (TargetFrameworkAttribute(project, version, moniker, frameworkDirectory) is { } generated)
        {
            trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(generated.Text), parseOptions, generated.Path, ct));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, trees, references, BareCompilationOptions.Compilation(properties, version));
        ImmutableArray<LoadDiagnostic> diagnostics =
        [
            .. compilation.GetDiagnostics(ct)
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new LoadDiagnostic(CompilationDiagnosticClassifier.Classify(d.Id), d.Id, name, d.GetMessage(CultureInfo.InvariantCulture))),
        ];
        return new BareProject(name, project.Path, assemblyName, IsCSharp: true, compilation, diagnostics);
    }

    /// <summary>The project's .NET Framework: version (default <c>v4.0</c>, as MSBuild's), profile and moniker.</summary>
    private static (Version Version, string VersionText, string Profile, string Moniker) TargetFramework(MsBuildProperties properties)
    {
        string identifier = properties.Read("TargetFrameworkIdentifier").Trim();
        if (identifier.Length > 0 && !identifier.Equals(".NETFramework", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsupportedConstructException($"the target framework '{identifier}' (the bare loader loads .NET Framework projects only)");
        }

        string versionText = properties.Read("TargetFrameworkVersion").Trim() is { Length: > 0 } v ? v : "v4.0";
        Version version = versionText is ['v' or 'V', .. string number] && Version.TryParse(number, out Version? parsed)
            ? parsed
            : throw new UnsupportedConstructException($"the TargetFrameworkVersion '{versionText}'");
        string profile = properties.Read("TargetFrameworkProfile").Trim();
        return (version, versionText, profile, $".NETFramework,Version={versionText}{(profile.Length > 0 ? ",Profile=" + profile : string.Empty)}");
    }

    /// <summary>
    /// The PackageReference assets NuGet's restore resolved (none without PackageReference items), and the assets file
    /// looked at; null assets when the file is missing.
    /// </summary>
    private static async Task<(ProjectAssets? Assets, string AssetsFile)> PackageAssetsAsync(EvaluatedProject project, string moniker, CancellationToken ct)
    {
        if (!project.OfType("PackageReference").Any())
        {
            return (new ProjectAssets([], []), string.Empty);
        }

        string assetsFile = project.Properties.Read("ProjectAssetsFile").Trim() is { Length: > 0 } file
            ? file
            : System.IO.Path.Combine(ProjectPath.Full(project.Directory, ProjectExtensionsPath(project.Properties)), "project.assets.json");
        return ProjectPath.Resolve(project.Directory, assetsFile) is { } found
            ? (ProjectAssets.Read(await File.ReadAllTextAsync(found, ct).ConfigureAwait(false), moniker), assetsFile)
            : (null, assetsFile);
    }

    /// <summary>
    /// The project's references to other projects: an SDK-style one binds against the compilation MSBuildWorkspace built
    /// for it, a non-SDK one is loaded by this loader, and one that does not load is left out.
    /// </summary>
    private async Task<List<MetadataReference>> ProjectReferencesAsync(EvaluatedProject project, CancellationToken ct)
    {
        List<MetadataReference> references = [];
        foreach (EvaluatedItem reference in project.OfType("ProjectReference").Where(static r => !string.Equals(r.Metadatum("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)))
        {
            Compilation? target = sdkProjects.TryGetValue(reference.Include, out Compilation? sdk) ? sdk
                : SdkStyleProject.IsSdkStyleFile(reference.Include) ? throw new UnsupportedConstructException($"the reference to the SDK-style project '{reference.Include}', for which MSBuildWorkspace built no compilation")
                : (await LoadAsync(reference.Include, ct).ConfigureAwait(false)).Compilation;
            if (target is not null)
            {
                references.Add(target.ToMetadataReference([.. (reference.Metadatum("Aliases") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]));
            }
        }

        return references;
    }

    private static async Task<List<SyntaxTree>> ParseAsync(ImmutableArray<string> sources, CSharpParseOptions options, CancellationToken ct)
    {
        List<SyntaxTree> trees = [];
        foreach (string source in sources)
        {
            FileStream stream = File.OpenRead(source);
            await using (stream.ConfigureAwait(false))
            {
                trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(stream), options, source, ct));
            }
        }

        return trees;
    }

    /// <summary>
    /// <c>obj/Debug/&lt;moniker&gt;.AssemblyAttributes.cs</c>, which MSBuild writes and compiles for every project from
    /// .NET Framework 4.0 (not for the CLR 2 frameworks): the file on disk when a build left one, else its text as
    /// MSBuild's <c>WriteCodeFragment</c> would write it.
    /// </summary>
    private static (string Path, string Text)? TargetFrameworkAttribute(EvaluatedProject project, Version version, string moniker, string frameworkDirectory)
    {
        string generate = project.Properties.Read("GenerateTargetFrameworkAttribute").Trim();
        if (generate.Length == 0 ? version < new Version(4, 0) : !generate.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string intermediate = project.Properties.Read("IntermediateOutputPath").Trim() is { Length: > 0 } configured
            ? configured
            : (project.Properties.Read("BaseIntermediateOutputPath").Trim() is { Length: > 0 } baseIntermediate ? baseIntermediate : @"obj\") + @"Debug\";
        string path = ProjectPath.Full(project.Directory, System.IO.Path.Combine(MsBuildProperties.Unescape(intermediate), $"{moniker}.AssemblyAttributes.cs"));
        if (File.Exists(path))
        {
            return (path, File.ReadAllText(path));
        }

        string displayName = ProjectPath.Resolve(frameworkDirectory, "RedistList/FrameworkList.xml") is { } list
            ? XDocument.Load(list).Root!.Attribute("Name")?.Value ?? string.Empty
            : string.Empty;
        return (path, $"""
            // <autogenerated />
            using System;
            using System.Reflection;
            [assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute("{moniker}", FrameworkDisplayName = "{displayName}")]

            """);
    }

    private static string ProjectExtensionsPath(MsBuildProperties properties) =>
        MsBuildProperties.Unescape(properties.Read("MSBuildProjectExtensionsPath").Trim() is { Length: > 0 } configured ? configured
            : properties.Read("BaseIntermediateOutputPath").Trim() is { Length: > 0 } baseIntermediate ? baseIntermediate
            : @"obj\");

    private static BareProject Failed(string projectPath, string reason)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(projectPath);
        LoadDiagnostic failure = new(LoadDiagnosticKind.WorkspaceFailure, string.Empty, name, $"the bare loader skipped '{projectPath}': {reason}");
        return new BareProject(name, projectPath, name, IsCSharp: true, Compilation: null, [failure]);
    }
}
