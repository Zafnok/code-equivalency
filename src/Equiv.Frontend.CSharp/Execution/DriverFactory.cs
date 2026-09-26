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
/// Resolves a BCL member against each runtime's reference assemblies and compiles its driver twice (ADR 0035, ticket
/// M3-032): a .NET Framework 4.8 console executable with an <c>app.config</c> naming <c>supportedRuntime v4.0</c>, and
/// a .NET 10 console assembly with a <c>runtimeconfig.json</c>, run through <c>dotnet</c>. Members are named by their
/// <see cref="CallIdentity"/> (<see cref="RoslynIdentity"/>), so a runtime-changes row's prefix selects them directly. Each
/// driver's source is written beside it as <c>EquivDriver.cs</c>, for whoever reads a witness.
/// </summary>
public sealed class DriverFactory : IExecutionDriverFactory
{
    private const string AssemblyName = "EquivDriver";

    private const string AppConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <startup>
            <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
          </startup>
        </configuration>
        """;

    private const string RuntimeConfig = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
          }
        }
        """;

    private readonly Lazy<Runtime> legacy;

    private readonly Lazy<Runtime> modern;

    /// <summary>A factory over the installed reference assemblies, located on first use.</summary>
    public DriverFactory()
        : this(DriverReferences.Installed)
    {
    }

    internal DriverFactory(Func<DriverReferences> references)
    {
        Lazy<DriverReferences> located = new(references);
        legacy = new(() => new Runtime(".NET Framework 4.8", located.Value.Legacy, LanguageVersion.CSharp7_3));
        modern = new(() => new Runtime(".NET 10", located.Value.Modern, LanguageVersion.Latest));
    }

    public IReadOnlyList<ExecutionSignature> Resolve(string member)
    {
        ArgumentNullException.ThrowIfNull(member);

        Dictionary<string, IMethodSymbol> old = Members(legacy.Value, member);
        Dictionary<string, IMethodSymbol> @new = Members(modern.Value, member);
        return [.. @new.Keys.Where(old.ContainsKey).Order(StringComparer.Ordinal).Select(id => Describe(id, @new[id], old[id]))];
    }

    public ExecutionDrivers Create(ExecutionRequest request, string directory)
    {
        ArgumentNullException.ThrowIfNull(request);

        string legacyDriver = Emit(legacy.Value, request.Member.Value, Path.Combine(directory, "legacy", AssemblyName + ".exe"));
        File.WriteAllText(legacyDriver + ".config", AppConfig);
        string modernDriver = Emit(modern.Value, request.Member.Value, Path.Combine(directory, "modern", AssemblyName + ".dll"));
        File.WriteAllText(Path.ChangeExtension(modernDriver, ".runtimeconfig.json"), RuntimeConfig);
        return new ExecutionDrivers(legacyDriver, modernDriver);
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
    }

    /// <summary>A parameter, with an enum's defined values taken from both runtimes, since a runtime may add members.</summary>
    private static ExecutionParameter Parameter(ITypeSymbol type, RefKind refKind, ITypeSymbol legacyType)
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

    private static string Emit(Runtime runtime, string identity, string path)
    {
        if (!Members(runtime, identity).TryGetValue(identity, out IMethodSymbol? method))
        {
            throw new InvalidOperationException($"{identity} is not a public member on {runtime.Name}");
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
            throw new InvalidOperationException($"the {runtime.Name} driver for {identity} does not compile: {string.Join("; ", errors)}");
        }

        return path;
    }

    /// <summary>One runtime's reference assemblies, as an empty compilation that driver source is added to.</summary>
    private sealed class Runtime
    {
        public Runtime(string name, IReadOnlyList<string> references, LanguageVersion languageVersion)
        {
            if (references.Count == 0)
            {
                throw new InvalidOperationException($"no {name} reference assemblies are installed");
            }

            Name = name;
            Parse = new CSharpParseOptions(languageVersion);
            Probe = CSharpCompilation.Create(
                AssemblyName,
                [],
                [.. references.Select(static r => MetadataReference.CreateFromFile(r))],
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        }

        public string Name { get; }

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
