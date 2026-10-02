using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Equiv.Frontend.CSharp.Execution;

/// <summary>
/// Emits a loaded project's <see cref="Compilation"/> for replay (ADR 0035 decision 2; ticket M4-009), with what it needs
/// at run time next to it: each project it references, emitted the same way, and each referenced file outside a reference
/// pack. A reference assembly (<c>ReferenceAssemblyAttribute</c>, as the .NET Framework targeting pack and the .NET
/// reference packs are) cannot run, and the runtime supplies the real one; the facades beside it in its folder only
/// forward types to it, so any folder that holds a reference assembly is a pack and nothing in it is copied.
/// <para>
/// Each project is emitted with one more syntax tree than it was loaded with: an <c>InternalsVisibleTo</c> naming the
/// replay driver's assembly (ticket P2-052), so that generated source can call its <c>internal</c> and
/// <c>protected internal</c> members directly on both runtimes. No file on disk changes. A strong-named project may
/// name a friend only with its public key, so its attribute carries the run's <see cref="DriverKey"/>.
/// </para>
/// </summary>
internal static class ProjectEmitter
{
    /// <summary>The assembly name of every replay driver, whatever its file is called: the friend each emitted project names.</summary>
    public const string DriverAssembly = "EquivReplay";

    /// <summary>
    /// Emits <paramref name="compilation"/> into <paramref name="directory"/>. Returns the compilation that was emitted,
    /// which is the one a driver compiles against, and the first error, or null on success.
    /// </summary>
    public static (Compilation Granted, string? Error) Emit(Compilation compilation, string directory, DriverKey key)
    {
        Directory.CreateDirectory(directory);
        Compilation granted = Grant(compilation, key);
        return (granted, Emit(granted, directory, key, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileName(compilation) }));
    }

    /// <summary><paramref name="compilation"/> with the driver as its friend.</summary>
    private static Compilation Grant(Compilation compilation, DriverKey key)
    {
        string friend = compilation.Assembly.Identity.HasPublicKey
            ? $"{DriverAssembly}, PublicKey={Convert.ToHexString(key.PublicKey.AsSpan())}"
            : DriverAssembly;
        return compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            SourceText.From($"[assembly: global::System.Runtime.CompilerServices.InternalsVisibleTo(\"{friend}\")]", Encoding.UTF8),
            compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions));
    }

    /// <summary>Emits a referenced project, once, with the driver as its friend.</summary>
    private static string? Referenced(Compilation compilation, string directory, DriverKey key, HashSet<string> done) =>
        done.Add(FileName(compilation)) ? Emit(Grant(compilation, key), directory, key, done) : null;

    private static string? Emit(Compilation compilation, string directory, DriverKey key, HashSet<string> done)
    {
        List<(IAssemblySymbol Assembly, MetadataReference? Reference)> references =
            [.. compilation.SourceModule.ReferencedAssemblySymbols.Select(a => (a, compilation.GetMetadataReference(a)))];
        HashSet<string> packs = new(
            references.Where(static r => ReferenceAssemblies.IsReferenceAssembly(r.Assembly)).Select(static r => r.Reference).OfType<PortableExecutableReference>()
                .Select(static r => r.FilePath).OfType<string>().Select(Folder),
            StringComparer.OrdinalIgnoreCase);
        foreach ((IAssemblySymbol _, MetadataReference? reference) in references)
        {
            string? error = reference switch
            {
                CompilationReference project => Referenced(project.Compilation, directory, key, done),
                PortableExecutableReference { FilePath: { } path } when !packs.Contains(Folder(path)) => Copy(path, directory, done),
                _ => null,
            };
            if (error is not null)
            {
                return error;
            }
        }

        EmitResult result = compilation.Emit(Path.Combine(directory, FileName(compilation)));
        return result.Success
            ? null
            : $"{compilation.AssemblyName}: {string.Join("; ", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error))}";
    }

    /// <summary>
    /// Every project is emitted as <c>&lt;assembly&gt;.dll</c>, an executable one too: both runtimes probe the application
    /// folder for that name first.
    /// </summary>
    private static string FileName(Compilation compilation) => compilation.AssemblyName + ".dll";

    private static string Folder(string path) => Path.GetDirectoryName(path)!;

    private static string? Copy(string path, string directory, HashSet<string> done)
    {
        string name = Path.GetFileName(path);
        if (done.Add(name))
        {
            File.Copy(path, Path.Combine(directory, name), overwrite: true);
        }

        return null;
    }
}
