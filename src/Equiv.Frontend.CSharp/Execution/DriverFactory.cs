using System.Collections.Immutable;
using System.Globalization;

using Equiv.Core;
using Equiv.Core.Configuration;
using Equiv.Core.Execution;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Resolves a BCL member against each side's runtime's reference assemblies and compiles its driver for that runtime (ADR
/// 0035, ADR 0040 decision 3; tickets M3-032, P2-056): an <c>.exe</c> with an <c>app.config</c> for .NET Framework, a
/// <c>.dll</c> with a <c>runtimeconfig.json</c> for .NET, run through <c>dotnet</c> (<see cref="DriverRuntime"/>). The
/// defaults are .NET Framework 4.8 against .NET 10. A runtime that is not installed makes every member not constructible,
/// with the reason <c>runtime &lt;tfm&gt; not installed</c>. The Windows Desktop assemblies (<c>System.Windows.Forms</c>,
/// <c>System.Drawing</c>) count as the runtime on both sides (ticket P2-051): a .NET driver that uses one targets
/// <c>net&lt;v&gt;-windows</c> and runs on <c>Microsoft.WindowsDesktop.App</c>. Members are named by their
/// <see cref="CallIdentity"/> (<see cref="RoslynIdentity"/>), so a runtime-changes row's prefix selects them directly. Each
/// driver's source is written beside it as <c>EquivDriver.cs</c>, for whoever reads a witness.
/// </summary>
public sealed class DriverFactory : IExecutionDriverFactory
{
    private const string AssemblyName = "EquivDriver";

    /// <summary>Why a member whose receiver or parameter is a window, or that drives the desktop session, is not run.</summary>
    internal const string NeedsWindow = "needs a live window handle or a message loop";

    private readonly Lazy<Runtime?> legacy;

    private readonly Lazy<Runtime?> modern;

    private readonly TargetRuntime legacyTarget;

    private readonly TargetRuntime modernTarget;

    /// <summary>A factory for .NET Framework 4.8 against .NET 10 over the installed runtimes, located on first use.</summary>
    public DriverFactory()
        : this(DefaultLegacy, DefaultModern)
    {
    }

    /// <summary>A factory for <paramref name="legacy"/> against <paramref name="modern"/> over the installed runtimes, located on first use.</summary>
    public DriverFactory(TargetRuntime legacy, TargetRuntime modern)
        : this(legacy, modern, static target => DriverReferences.Installed().For(target))
    {
    }

    internal DriverFactory(TargetRuntime legacy, TargetRuntime modern, Func<TargetRuntime, DriverRuntime?> locate)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(modern);

        legacyTarget = legacy;
        modernTarget = modern;
        this.legacy = new(() => locate(legacy) is { } runtime ? new Runtime(runtime) : null);
        this.modern = new(() => locate(modern) is { } runtime ? new Runtime(runtime) : null);
    }

    /// <summary>The runtime <c>runtime-diff</c> runs its legacy side on without <c>--from</c>.</summary>
    public static TargetRuntime DefaultLegacy { get; } = new(TargetRuntime.RuntimeFamily.NetFramework, new Version(4, 8));

    /// <summary>The runtime <c>runtime-diff</c> runs its modern side on without <c>--to</c>.</summary>
    public static TargetRuntime DefaultModern { get; } = new(TargetRuntime.RuntimeFamily.NetCore, new Version(10, 0));

    public IReadOnlyList<ExecutionSignature> Resolve(string member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (Missing() is { } missing)
        {
            return [new ExecutionSignature(new CallIdentity(member), [], [missing])];
        }

        Dictionary<string, IMethodSymbol> old = Members(legacy.Value!, member);
        Dictionary<string, IMethodSymbol> @new = Members(modern.Value!, member);
        return [.. @new.Keys.Where(old.ContainsKey).Order(StringComparer.Ordinal).Select(id => Describe(id, @new[id], old[id]))];
    }

    public ExecutionDrivers Create(ExecutionRequest request, string directory)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Missing() is { } missing
            ? throw new InvalidOperationException(missing)
            : new ExecutionDrivers(
                Driver(legacy.Value!, request.Member.Value, Path.Combine(directory, "legacy")),
                Driver(modern.Value!, request.Member.Value, Path.Combine(directory, "modern")));
    }

    /// <summary>The not-constructible reason for a runtime that is not installed (ADR 0040 decision 3).</summary>
    internal static string NotInstalled(TargetRuntime runtime) => $"runtime {runtime} not installed";

    /// <summary>The reason no driver can be built, when a side's runtime or its reference assemblies are not installed.</summary>
    private string? Missing() => (legacy.Value, modern.Value) switch
    {
        (null, _) => NotInstalled(legacyTarget),
        (_, null) => NotInstalled(modernTarget),
        _ => null,
    };

    /// <summary>Compiles the driver for <paramref name="identity"/> into <paramref name="directory"/>, with its runtime's configuration beside it.</summary>
    private static string Driver(Runtime runtime, string identity, string directory)
    {
        string path = Path.Combine(directory, AssemblyName + runtime.Host.Extension);
        runtime.Host.WriteConfig(path, Emit(runtime, identity, path));
        return path;
    }

    /// <summary>What the input generators can build for <paramref name="type"/>.</summary>
    internal static ExecutionTypeKind Classify(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean => ExecutionTypeKind.Boolean,
        SpecialType.System_Char => ExecutionTypeKind.Character,
        SpecialType.System_SByte => ExecutionTypeKind.SignedByte,
        SpecialType.System_Byte => ExecutionTypeKind.UnsignedByte,
        SpecialType.System_Int16 => ExecutionTypeKind.Signed16,
        SpecialType.System_UInt16 => ExecutionTypeKind.Unsigned16,
        SpecialType.System_Int32 => ExecutionTypeKind.Signed32,
        SpecialType.System_UInt32 => ExecutionTypeKind.Unsigned32,
        SpecialType.System_Int64 => ExecutionTypeKind.Signed64,
        SpecialType.System_UInt64 => ExecutionTypeKind.Unsigned64,
        SpecialType.System_Single => ExecutionTypeKind.Binary32,
        SpecialType.System_Double => ExecutionTypeKind.Binary64,
        SpecialType.System_Decimal => ExecutionTypeKind.DecimalNumber,
        SpecialType.System_String => ExecutionTypeKind.Text,
        _ when type.TypeKind == TypeKind.Enum => ExecutionTypeKind.Enum,
        _ when type.IsReferenceType => ExecutionTypeKind.NullOnly,
        _ => Drawing(type),
    };

    /// <summary>The <c>System.Drawing</c> value types the generators build from their components (ticket P2-051).</summary>
    private static ExecutionTypeKind Drawing(ITypeSymbol type) => type.ToDisplayString() switch
    {
        "System.Drawing.Point" or "System.Drawing.Size" => ExecutionTypeKind.Signed32Pair,
        "System.Drawing.Rectangle" => ExecutionTypeKind.Signed32Quad,
        "System.Drawing.PointF" or "System.Drawing.SizeF" => ExecutionTypeKind.Binary32Pair,
        "System.Drawing.RectangleF" => ExecutionTypeKind.Binary32Quad,
        _ => ExecutionTypeKind.Unsupported,
    };

    /// <summary>An instance member other than a constructor takes its receiver as argument 0.</summary>
    internal static bool HasReceiver(IMethodSymbol method) => !method.IsStatic && method.MethodKind != MethodKind.Constructor;

    private static ExecutionSignature Describe(string identity, IMethodSymbol method, IMethodSymbol legacyMethod)
    {
        List<ExecutionParameter> parameters = [];
        if (HasReceiver(method))
        {
            parameters.Add(Parameter(method.ContainingType, RefKind.None, legacyMethod.ContainingType));
        }

        parameters.AddRange(method.Parameters.Select((p, i) => Parameter(p.Type, p.RefKind, legacyMethod.Parameters[i].Type)));
        List<string> notConstructible = [.. Obstacles(method)];
        notConstructible.AddRange(parameters.Where(static p => p.Kind == ExecutionTypeKind.Unsupported).Select(static p => p.TypeName).Distinct(StringComparer.Ordinal));
        return new ExecutionSignature(new CallIdentity(identity), parameters, notConstructible);
    }

    /// <summary>Why generated source cannot call <paramref name="method"/> at all, whatever its arguments.</summary>
    private static IEnumerable<string> Obstacles(IMethodSymbol method)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.Constructor))
        {
            yield return "not a method, a property getter or a constructor";
        }

        if (method.IsGenericMethod || method.ContainingType.IsGenericType)
        {
            yield return "generic";
        }

        if (method.IsVararg)
        {
            yield return "__arglist";
        }

        if (method.MethodKind == MethodKind.Constructor && method.ContainingType.IsAbstract)
        {
            yield return "constructor of an abstract type";
        }

        if (method.ReturnType.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
        {
            yield return "returns a pointer";
        }

        if (IsDesktopSession(method.ContainingType) || (HasReceiver(method) && IsWindow(method.ContainingType)) || method.Parameters.Any(static p => IsWindow(p.Type)))
        {
            yield return NeedsWindow;
        }
    }

    /// <summary>A window (<c>IWin32Window</c>, which every <c>Control</c> is) or a window message.</summary>
    private static bool IsWindow(ITypeSymbol type) =>
        type.ToDisplayString() is WindowInterface or "System.Windows.Forms.Message"
        || type.AllInterfaces.Any(static i => string.Equals(i.ToDisplayString(), WindowInterface, StringComparison.Ordinal));

    private const string WindowInterface = "System.Windows.Forms.IWin32Window";

    /// <summary>A type whose static members run or post to a message loop, or act on the user's desktop session.</summary>
    private static bool IsDesktopSession(INamedTypeSymbol type) => type.ToDisplayString() is
        "System.Windows.Forms.Application" or "System.Windows.Forms.Clipboard" or "System.Windows.Forms.Cursor" or "System.Windows.Forms.MessageBox" or "System.Windows.Forms.SendKeys";

    /// <summary>A parameter, with an enum's defined values taken from both runtimes, since a runtime may add members.</summary>
    internal static ExecutionParameter Parameter(ITypeSymbol type, RefKind refKind, ITypeSymbol legacyType)
    {
        string prefix = refKind switch
        {
            RefKind.None => string.Empty,
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => "ref ",
        };
        ExecutionTypeKind kind = refKind == RefKind.None ? Classify(type) : ExecutionTypeKind.Unsupported;
        IReadOnlyList<string> values = kind == ExecutionTypeKind.Enum
            ? [.. EnumValues(type).Union(EnumValues(legacyType), StringComparer.Ordinal)]
            : [];
        return new ExecutionParameter(prefix + type.ToDisplayString(), kind, values);
    }

    private static IEnumerable<string> EnumValues(ITypeSymbol type) =>
        type.GetMembers().OfType<IFieldSymbol>()
            .Where(static f => f.HasConstantValue)
            .Select(static f => Convert.ToString(f.ConstantValue, CultureInfo.InvariantCulture)!);

    /// <summary>Every public member of the type <paramref name="member"/> names whose identity starts with it.</summary>
    private static Dictionary<string, IMethodSymbol> Members(Runtime runtime, string member)
    {
        Dictionary<string, IMethodSymbol> members = new(StringComparer.Ordinal);
        int separator = member.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0 || runtime.FindType(member[..separator]) is not { } type)
        {
            return members;
        }

        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(static m => m.DeclaredAccessibility == Accessibility.Public))
        {
            string identity = RoslynIdentity.Of(method, RenameMap.Empty).Value;
            if (identity.StartsWith(member, StringComparison.Ordinal))
            {
                members.TryAdd(identity, method);
            }
        }

        return members;
    }

    /// <summary>Compiles the driver for <paramref name="identity"/> to <paramref name="path"/>; true when it uses a Windows Desktop assembly.</summary>
    private static bool Emit(Runtime runtime, string identity, string path)
    {
        if (!Members(runtime, identity).TryGetValue(identity, out IMethodSymbol? method))
        {
            throw new InvalidOperationException($"{identity} is not a public member on {runtime.Host.Target}");
        }

        string source = DriverSource.Generate(method);
        CSharpCompilation compilation = runtime.Probe.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, runtime.Parse));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(Path.ChangeExtension(path, ".cs"), source);
        EmitResult result;
        using (FileStream image = File.Create(path))
        {
            result = compilation.Emit(image);
        }

        if (!result.Success)
        {
            IEnumerable<Diagnostic> errors = result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException($"the {runtime.Host.Target} driver for {identity} does not compile: {string.Join("; ", errors)}");
        }

        return compilation.GetUsedAssemblyReferences().OfType<PortableExecutableReference>().Any(r => runtime.Desktop.Contains(r.FilePath!));
    }

    /// <summary>
    /// One runtime's reference assemblies, as an empty compilation that driver source is added to. A Windows Desktop
    /// assembly replaces the base pack's one of the same file name (<c>System.Drawing.dll</c>, <c>WindowsBase.dll</c>),
    /// as the SDK's conflict resolution does for a <c>net&lt;v&gt;-windows</c> project.
    /// </summary>
    private sealed class Runtime
    {
        public Runtime(DriverRuntime host)
        {
            Host = host;
            Desktop = new HashSet<string>(host.Desktop, StringComparer.OrdinalIgnoreCase);
            HashSet<string> replaced = new(host.Desktop.Select(static d => Path.GetFileName(d)), StringComparer.OrdinalIgnoreCase);
            Parse = new CSharpParseOptions(host.Language);
            Probe = CSharpCompilation.Create(
                AssemblyName,
                [],
                [.. host.References.Where(r => !replaced.Contains(Path.GetFileName(r))).Concat(host.Desktop).Select(static r => MetadataReference.CreateFromFile(r))],
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        }

        public DriverRuntime Host { get; }

        /// <summary>The Windows Desktop reference assemblies' paths.</summary>
        public HashSet<string> Desktop { get; }

        public CSharpParseOptions Parse { get; }

        public CSharpCompilation Probe { get; }

        /// <summary>
        /// The public type an identity's type part names. The identity spells a nested type with <c>.</c> where metadata
        /// uses <c>+</c>, so each split point is tried, innermost first.
        /// </summary>
        public INamedTypeSymbol? FindType(string name)
        {
            ImmutableArray<int> dots = [.. name.Select(static (c, i) => (c, i)).Where(static p => p.c == '.').Select(static p => p.i).Reverse()];
            for (int nested = 0; nested <= dots.Length; nested++)
            {
                char[] metadataName = name.ToCharArray();
                foreach (int dot in dots.Take(nested))
                {
                    metadataName[dot] = '+';
                }

                if (Probe.GetTypesByMetadataName(new string(metadataName)).FirstOrDefault(IsPublic) is { } type)
                {
                    return type;
                }
            }

            return null;
        }

        private static bool IsPublic(INamedTypeSymbol type) =>
            type.DeclaredAccessibility == Accessibility.Public && (type.ContainingType is null || IsPublic(type.ContainingType));
    }
}
